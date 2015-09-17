using System;
using UnityEditor.MemoryProfiler;

namespace MemoryProfilerWindow
{
	static class ManagedHeapExtensions
	{
		public static BytesAndOffset Find(this MemorySection[] heap, UInt64 address, VirtualMachineInformation virtualMachineInformation)
		{
			foreach (var segment in heap)
				if (address >= segment.startAddress && address < (segment.startAddress + (ulong)segment.bytes.Length))
					return new BytesAndOffset() { bytes = segment.bytes, offset = (int)(address - segment.startAddress), pointerSize = virtualMachineInformation.pointerSize };

			return new BytesAndOffset();
		}

		public static int ReadArrayLength(this MemorySection[] heap, UInt64 address, TypeDescription arrayType, VirtualMachineInformation virtualMachineInformation)
		{
			var bo = heap.Find(address, virtualMachineInformation);

			var bounds = bo.Add(virtualMachineInformation.arrayBoundsOffsetInHeader).ReadPointer();

			if (bounds == 0)
				return bo.Add(virtualMachineInformation.arraySizeOffsetInHeader).ReadInt32();

			var cursor = heap.Find(bounds, virtualMachineInformation);
			int length = 0;
			for (int i = 0; i != arrayType.arrayRank; i++)
			{
				length += cursor.ReadInt32();
				cursor = cursor.Add(8);
			}
			return length;
		}
	}
}
