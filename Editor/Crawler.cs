using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace UnityEditor.Profiler.Memory
{
	[Serializable]
	public class PackedCrawledMemorySnapshot : PackedMemorySnapshot
	{
		public PackedManagedObject[] managedObjects;
		public PackedStaticFields[] packedStaticFields;
	}

	[Serializable]
	public class PackedManagedObject
	{
		public UInt64 address;
		public int typeIndex;
		public int size;
	}

	[Serializable]
	public class PackedStaticFields
	{
		public int typeIndex;
	}

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

			for (int i = 0; i != result.gcHandles.Length; i++)
				CrawlPointer(result.gcHandles[i].target, indexOfFirstGCHandle + i);

			for (int i = 0; i != result.packedStaticFields.Length; i++)
			{
				var typeDescription = input.typeDescriptions[result.packedStaticFields[i].typeIndex];
				CrawlRawObjectData(new BytesAndOffset() {bytes = typeDescription.staticFieldBytes, offset = 0}, typeDescription, true, indexOfFirstStaticFields + i);
			}

			result.managedObjects = _managedObjects.ToArray();

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
			ParseObjectHeader(bo, out typeInfoAddress, out indexOfObject, out wasAlreadyCrawled);

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

		private void ParseObjectHeader(BytesAndOffset bo, out ulong typeInfoAddress, out int indexOfObject, out bool wasAlreadyCrawled)
		{
			var pointer1 = bo.ReadPointer();
			var pointer2 = bo.Add(_heap.virtualMachineInformation.pointerSize);

			if (HasMarkBit(pointer1) == 0)
			{
				wasAlreadyCrawled = false;
				indexOfObject = _managedObjects.Count + _indexOfFirstManagedObject;
				typeInfoAddress = pointer1;
				_managedObjects.Add(new PackedManagedObject() {address = bo.originalHeapAddress, size = 0, typeIndex = _typeInfoToTypeDescription[pointer1].typeIndex});
			
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

	internal struct BytesAndOffset
	{
		public byte[] bytes;
		public int offset;
		public int pointerSize;
		public UInt64 originalHeapAddress;
		public bool IsValid { get { return bytes != null; }}

		public UInt64 ReadPointer()
		{
			if (pointerSize == 4)
				return BitConverter.ToUInt32(bytes, offset);
			if (pointerSize == 8)
				return BitConverter.ToUInt64(bytes, offset);
			throw new ArgumentException("Unexpected pointersize: " + pointerSize);
		}

		public Int32 ReadInt32()
		{
			return BitConverter.ToInt32(bytes, offset);
		}

		public BytesAndOffset Add(int add)
		{
			return new BytesAndOffset() {bytes = bytes, offset = offset + add, pointerSize = pointerSize};
		}

		public void WritePointer(UInt64 value)
		{
			bytes[offset+0] = (byte)(value >> 24);
			bytes[offset+1] = (byte)(value >> 16);
			bytes[offset+2] = (byte)(value >> 8);
			bytes[offset+3] = (byte)(value);
		}

		public BytesAndOffset NextPointer()
		{
			return Add(pointerSize);
		}
	}

	static class ManagedHeapExtensions
	{
		public static BytesAndOffset Find(this ManagedHeap heap, UInt64 address)
		{
			foreach(var segment in heap.segments)
				if (address >= segment.startAddress && address < (segment.startAddress + (ulong) segment.bytes.Length))
					return new BytesAndOffset() { bytes = segment.bytes, offset = (int)(address - segment.startAddress), originalHeapAddress = address };

			return new BytesAndOffset();
		}

		public static int ReadArrayLength(this ManagedHeap heap, UInt64 address, TypeDescription arrayType)
		{
			var bo = heap.Find(address);

			var bounds = bo.Add(heap.virtualMachineInformation.arrayBoundsOffsetInHeader).ReadPointer();

			if (bounds == 0)
				return bo.Add(heap.virtualMachineInformation.arraySizeOffsetInHeader).ReadInt32();

			var cursor = heap.Find(bounds);
			int length = 0;
			for (int i = 0; i != arrayType.ArrayRank; i++)
			{
				length += cursor.ReadInt32();
				cursor = cursor.Add(8);
			}
			return length;
		}
	}
}
