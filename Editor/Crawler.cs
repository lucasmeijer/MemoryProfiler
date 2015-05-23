using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEditor.Profiler.Memory
{
	internal class Crawler
	{
		private ManagedHeap _heap;
		private List<Connection> _connections;
		private TypeDescription[] _typeDescriptions;
		private Dictionary<UInt64, TypeDescription> _typeInfoToTypeDescription;
		private int _indexOfFirstManagedObject;
		private List<PackedManagedObject> _managedObjects;
		private Dictionary<int, UInt64> _pointer2Backups = new Dictionary<int, ulong>(); 

		public PackedCrawledMemorySnapshot Crawl(PackedMemorySnapshot input)
		{
			_heap = input.managedHeap;
			_typeDescriptions = input.typeDescriptions;
			_typeInfoToTypeDescription = _typeDescriptions.ToDictionary(td => td.typeInfoAddress, td => td);

			var result = new PackedCrawledMemorySnapshot
			{
				managedHeap = input.managedHeap,
				nativeObjects = input.nativeObjects,
				gcHandles = input.gcHandles,
				packedStaticFields = Enumerable.Range(0, input.typeDescriptions.Length).Where(i => input.typeDescriptions[i].staticFieldBytes.Length > 0).Select(i => new PackedStaticFields() {typeIndex = i}).ToArray()
			};

			var indexOfFirstGCHandle = result.nativeObjects.Length;
			int indexOfFirstStaticFields = indexOfFirstGCHandle + result.gcHandles.Length;
			_indexOfFirstManagedObject = indexOfFirstStaticFields + result.packedStaticFields.Length;

			_managedObjects = new List<PackedManagedObject>(_indexOfFirstManagedObject * 3);

			_connections = new List<Connection>(_managedObjects.Count * 3);
			//we will be adding a lot of connections, but the input format also already had connections. (nativeobject->nativeobject and nativeobject->gchandle). we'll add ours to the ones already there.
			_connections.AddRange(input.connections);
		
			
			for (int i = 0; i != result.gcHandles.Length; i++)
				CrawlPointer(result.gcHandles[i].target, indexOfFirstGCHandle + i);

			for (int i = 0; i != result.packedStaticFields.Length; i++)
			{
				var typeDescription = input.typeDescriptions[result.packedStaticFields[i].typeIndex];
				CrawlRawObjectData(new BytesAndOffset() {bytes = typeDescription.staticFieldBytes, offset = 0}, typeDescription, true, indexOfFirstStaticFields + i);
			}

			result.managedObjects = _managedObjects.ToArray();
			result.connections = _connections.ToArray();

			AddManagedToNativeConnectionsAndRestoreObjectHeaders(result.managedObjects, result.nativeObjects);

			return result;
		}

		private void AddManagedToNativeConnectionsAndRestoreObjectHeaders(PackedManagedObject[] managedObjects, PackedNativeUnityEngineObject[] nativeObjects)
		{
			var unityEngineObjectTypeDescription = _typeDescriptions.First(td => td.name == "UnityEngine.Object");
			var instanceIDOffset = unityEngineObjectTypeDescription.fields.Single(f => f.name == "m_InstanceID").offset;
			for (int i = 0; i != managedObjects.Length; i++)
			{
				var managedObjectIndex = i + _indexOfFirstManagedObject;
				var managedObject = managedObjects[i];
				var typeInfoAddress = RestoreObjectHeader(managedObject, managedObjectIndex);

				if (DerivesFrom(_typeInfoToTypeDescription[typeInfoAddress].typeIndex, unityEngineObjectTypeDescription.typeIndex))
				{
					var instanceID = _heap.Find(managedObject.address + (UInt64) instanceIDOffset).ReadInt32();
					var indexOfNativeObject = Array.FindIndex(nativeObjects, no => no.instanceID == instanceID);
					if (indexOfNativeObject != -1)
						_connections.Add(new Connection() {@from = managedObjectIndex, to = indexOfNativeObject});
				}
			}
		}

		private bool DerivesFrom(int typeIndex, int potentialBase)
		{
			if (typeIndex == potentialBase)
				return true;
			var baseIndex = _typeDescriptions[typeIndex].baseOrElementTypeIndex;
			if (baseIndex == unchecked((uint)-1))
				return false;
			return DerivesFrom(baseIndex, potentialBase);
		}

		private ulong RestoreObjectHeader(PackedManagedObject managedObject, int managedObjectIndex)
		{
			var bo = _heap.Find(managedObject.address);
			var typeInfoAddress = bo.ReadPointer() & unchecked((ulong)-1);
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

				var fieldType = _typeDescriptions[field.typeIndex];

				var fieldLocation = bytesAndOffset.Add(field.offset);

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
			var bo = _heap.Find(pointer);
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
				CrawlRawObjectData(bo.Add(_heap.virtualMachineInformation.objectHeaderSize), typeDescription, false, indexOfObject);
				return;
			}

			var arrayLength = _heap.ReadArrayLength(pointer, typeDescription);
			var elementType = _typeDescriptions[typeDescription.baseOrElementTypeIndex];
			var cursor = bo.Add(_heap.virtualMachineInformation.arrayHeaderSize);
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
			var pointer2 = bo.Add(_heap.virtualMachineInformation.pointerSize);

			if (HasMarkBit(pointer1) == 0)
			{
				wasAlreadyCrawled = false;
				indexOfObject = _managedObjects.Count + _indexOfFirstManagedObject;
				typeInfoAddress = pointer1;
				_managedObjects.Add(new PackedManagedObject() { address = originalHeapAddress, size = 0, typeIndex = _typeInfoToTypeDescription[pointer1].typeIndex });
			
				//okay, we gathered all information, now lets set the mark bit, and store the index for this object in the 2nd pointer of the header, which is rarely used.
				bo.WritePointer(pointer1 | 1);

				var oldValue = pointer2.ReadPointer();
				if (oldValue != 0)
					throw new Exception("there was a non0 value in the 2nd pointer of the object header. todo: implement backup scheme");
				
				//test writepointer implementation
				var magic = 0xdeadbeef1234abcd;
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
