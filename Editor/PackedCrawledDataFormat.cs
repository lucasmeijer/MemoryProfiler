using System;
using System.Text;

namespace UnityEditor.Profiler.Memory
{
	//this is the 2nd level data format: completely packed and serializable, but includes all the results from the crawler. so that means you have managed objects added,
	//and the connections have been filled in, as the PackedMemorySnapshot provided by the unity runtime only contains connections for native objects to other native objects,
	//and native objects to gchandles.


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
}
