using System;

namespace UnityEditor.Profiler.Memory
{
	internal struct BytesAndOffset
	{
		public byte[] bytes;
		public int offset;
		public int pointerSize;
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
}