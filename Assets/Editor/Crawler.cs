using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.MemoryProfiler;

namespace MemoryProfilerWindow
{
	internal class Crawler
	{
		private ManagedMemorySection[] _heaps;
		private List<Connection> _connections;
		private TypeDescription[] _typeDescriptions;
		private Dictionary<UInt64, TypeDescription> _typeInfoToTypeDescription;

		private List<PackedManagedObject> _managedObjects;
		private Dictionary<int, UInt64> _pointer2Backups = new Dictionary<int, ulong>();
		private VirtualMachineInformation _virtualMachineInformation;
		PackedCrawledMemorySnapshot _result;

		public PackedCrawledMemorySnapshot Crawl(PackedMemorySnapshot input)
		{
			_heaps = input.managedHeapSections;
			_typeDescriptions = input.typeDescriptions;
			_typeInfoToTypeDescription = _typeDescriptions.ToDictionary(td => td.typeInfoAddress, td => td);
			_virtualMachineInformation = input.virtualMachineInformation;

			_result = new PackedCrawledMemorySnapshot
			{
				managedHeapSections = input.managedHeapSections,
				nativeObjects = input.nativeObjects,
				gcHandles = input.gcHandles,
				nativeTypes = input.nativeTypes,
				stacks = input.stacks,
				typeDescriptions = input.typeDescriptions,
				packedStaticFields = Enumerable.Range(0, input.typeDescriptions.Length).Where(i => input.typeDescriptions[i].staticFieldBytes.Length > 0).Select(i => new PackedStaticFields() {typeIndex = i}).ToArray()
			};


			_managedObjects = new List<PackedManagedObject>(_result.IndexOfFirstManagedObject * 3);

			_connections = new List<Connection>(_managedObjects.Count * 3);
			//we will be adding a lot of connections, but the input format also already had connections. (nativeobject->nativeobject and nativeobject->gchandle). we'll add ours to the ones already there.
			_connections.AddRange(input.connections);
		
			
			for (int i = 0; i != _result.gcHandles.Length; i++)
				CrawlPointer(_result.gcHandles[i].target, _result.IndexOfFirstGCHandle + i);

			for (int i = 0; i != _result.packedStaticFields.Length; i++)
			{
				var typeDescription = input.typeDescriptions[_result.packedStaticFields[i].typeIndex];
				CrawlRawObjectData(new BytesAndOffset {bytes = typeDescription.staticFieldBytes, offset = 0, pointerSize = _virtualMachineInformation.pointerSize}, typeDescription, true, _result.IndexOfFirstStaticFields + i);
			}

			_result.managedObjects = _managedObjects.ToArray();
			_result.connections = _connections.ToArray();

			AddManagedToNativeConnectionsAndRestoreObjectHeaders(_result.managedObjects, _result.nativeObjects);

			return _result;
		}

		private void AddManagedToNativeConnectionsAndRestoreObjectHeaders(PackedManagedObject[] managedObjects, PackedNativeUnityEngineObject[] nativeObjects)
		{
			var unityEngineObjectTypeDescription = _typeDescriptions.First(td => td.name == "UnityEngine.Object");
			var instanceIDOffset = unityEngineObjectTypeDescription.fields.Single(f => f.name == "m_InstanceID").offset;
			for (int i = 0; i != managedObjects.Length; i++)
			{
				var managedObjectIndex = i + _result.IndexOfFirstManagedObject;
				var address = managedObjects[i].address;

				var typeInfoAddress = RestoreObjectHeader(address, managedObjectIndex);

				if (!DerivesFrom(_typeInfoToTypeDescription[typeInfoAddress].typeIndex, unityEngineObjectTypeDescription.typeIndex)) 
					continue;

				var instanceID = _heaps.Find(address + (UInt64)instanceIDOffset, _virtualMachineInformation).ReadInt32();
				var indexOfNativeObject = Array.FindIndex(nativeObjects, no => no.instanceId == instanceID);
				if (indexOfNativeObject != -1)
					_connections.Add(new Connection {@from = managedObjectIndex, to = indexOfNativeObject});
			}
		}

		private bool DerivesFrom(int typeIndex, int potentialBase)
		{
			if (typeIndex == potentialBase)
				return true;
			var baseIndex = _typeDescriptions[typeIndex].baseOrElementTypeIndex;

			if (baseIndex == -1)
				return false;
			
			return DerivesFrom(baseIndex, potentialBase);
		}

		private ulong RestoreObjectHeader(ulong address, int managedObjectIndex)
		{
			var bo = _heaps.Find(address, _virtualMachineInformation);
			var mask = this._virtualMachineInformation.pointerSize == 8 ? System.UInt64.MaxValue - 1 : System.UInt32.MaxValue - 1;
			var pointer = bo.ReadPointer ();
			var typeInfoAddress = pointer & mask;
			bo.WritePointer(typeInfoAddress);

			UInt64 restoreValue = 0;
			_pointer2Backups.TryGetValue(managedObjectIndex, out restoreValue);
			bo.NextPointer().WritePointer(restoreValue);
			return typeInfoAddress;
		}

		private void CrawlRawObjectData(BytesAndOffset bytesAndOffset, TypeDescription typeDescription, bool useStaticFields, int indexOfFrom)
		{
			foreach (var field in typeDescription.fields)
			{
				if (field.isStatic != useStaticFields)
					continue;

				if (field.typeIndex == typeDescription.typeIndex && typeDescription.IsValueType) {
					//this happens in System.Single, which is a weird type that has a field of its own type.
					continue;
				}

				if (field.offset == -1) {
					//this is how we encode TLS fields. todo: actually treat TLS fields as roots
					continue;
				}

				var fieldType = _typeDescriptions[field.typeIndex];

				var fieldLocation = bytesAndOffset.Add(field.offset - (useStaticFields ? 0 : _virtualMachineInformation.objectHeaderSize));

				if (fieldType.IsValueType)
				{
					CrawlRawObjectData(fieldLocation, fieldType, false, indexOfFrom);
					continue;
				}

				CrawlPointer(fieldLocation.ReadPointer(), indexOfFrom);
			}
		}

		private void CrawlPointer(UInt64 pointer, int indexOfFrom)
		{
			var bo = _heaps.Find(pointer, _virtualMachineInformation);
			if (!bo.IsValid)
				return;

			UInt64 typeInfoAddress;
			int indexOfObject;
			bool wasAlreadyCrawled;
			ParseObjectHeader(bo, pointer, out typeInfoAddress, out indexOfObject, out wasAlreadyCrawled);

			_connections.Add(new Connection() {from = indexOfFrom, to = indexOfObject});

			if (wasAlreadyCrawled)
				return;

			var typeDescription = _typeInfoToTypeDescription[typeInfoAddress];

			if (!typeDescription.IsArray)
			{
				CrawlRawObjectData(bo.Add(_virtualMachineInformation.objectHeaderSize), typeDescription, false, indexOfObject);
				return;
			}

			var arrayLength = _heaps.ReadArrayLength(pointer, typeDescription,_virtualMachineInformation);
			var elementType = _typeDescriptions[typeDescription.baseOrElementTypeIndex];
			var cursor = bo.Add(_virtualMachineInformation.arrayHeaderSize);
			for (int i = 0; i != arrayLength; i++)
			{
				if (elementType.IsValueType)
				{
					CrawlRawObjectData(cursor, elementType, false, indexOfObject);
					cursor = cursor.Add(elementType.size);
				}
				else
				{
					CrawlPointer(cursor.ReadPointer(), indexOfObject);
					cursor = cursor.NextPointer();
				}
			}
		}

		private void ParseObjectHeader(BytesAndOffset bo, UInt64 originalHeapAddress, out ulong typeInfoAddress, out int indexOfObject, out bool wasAlreadyCrawled)
		{
			var pointer1 = bo.ReadPointer();
			var pointer2 = bo.NextPointer();

			if (HasMarkBit(pointer1) == 0)
			{
				wasAlreadyCrawled = false;
				indexOfObject = _managedObjects.Count + _result.IndexOfFirstManagedObject;
				typeInfoAddress = pointer1;
				var typeDescription = _typeInfoToTypeDescription [pointer1];
				var size = typeDescription.IsArray ? 0 : typeDescription.size;
				_managedObjects.Add(new PackedManagedObject() { address = originalHeapAddress, size = size, typeIndex = typeDescription.typeIndex });
			
				//okay, we gathered all information, now lets set the mark bit, and store the index for this object in the 2nd pointer of the header, which is rarely used.
				bo.WritePointer(pointer1 | 1);

				var oldValue = pointer2.ReadPointer();
				if (oldValue != 0)
					throw new Exception("there was a non0 value in the 2nd pointer of the object header. todo: implement backup scheme");
				
				//test writepointer implementation
				var magic = 0xdeadbeef;
				pointer2.WritePointer(magic);
				var check = pointer2.ReadPointer();
				if (check != magic)
					throw new Exception("writepointer broken");

				pointer2.WritePointer((ulong) indexOfObject);
				return;
			}

			//give typeinfo address back without the markbit
			typeInfoAddress = ClearMarkBit(pointer1);
			wasAlreadyCrawled = true;
			//read the index for this object that we stored in the 2ndpointer field of the header
			indexOfObject = (int)pointer2.ReadPointer();
		}

		private static ulong HasMarkBit(ulong pointer1)
		{
			return pointer1 & 1;
		}

		private static ulong ClearMarkBit(ulong pointer1)
		{
			return pointer1 & unchecked((ulong)-1);
		}
	}
}
