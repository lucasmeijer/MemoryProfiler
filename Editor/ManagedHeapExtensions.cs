using System;

namespace UnityEditor.Profiler.Memory
{
	static class ManagedHeapExtensions
	{
		public static BytesAndOffset Find(this ManagedHeap heap, UInt64 address)
		{
			foreach(var segment in heap.segments)
				if (address >= segment.startAddress && address < (segment.startAddress + (ulong) segment.bytes.Length))
					return new BytesAndOffset() { bytes = segment.bytes, offset = (int)(address - segment.startAddress) };

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