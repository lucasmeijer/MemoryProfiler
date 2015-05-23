using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UnityEditor.Profiler.Memory
{
	public class CrawledMemorySnapshot
	{
		public NativeUnityEngineObject[] nativeObjects;
		public GCHandle[] gcHandles;
		public ManagedObject[] managedObjects;
		public StaticFields[] staticFields;

		public ManagedHeap managedHeap;
		public TypeDescription[] typeDescriptions;
		public string[] classIDNames;

		static CrawledMemorySnapshot UnpackFrom(PackedCrawledMemorySnapshot packedSnapshot)
		{
			var result = new CrawledMemorySnapshot();

			result.nativeObjects = packedSnapshot.nativeObjects.Select(pn => UnpackNativeUnityEngineObject(packedSnapshot, pn)).ToArray();
			result.managedObjects = packedSnapshot.managedObjects.Select(pm => UnpackManagedObject(packedSnapshot, pm)).ToArray();
			result.gcHandles = packedSnapshot.gcHandles.Select(pgc => UnpackGCHandle(packedSnapshot, pgc)).ToArray();
			result.staticFields = packedSnapshot.packedStaticFields.Select(psf => UnpackStaticFields(packedSnapshot, psf)).ToArray();
			result.typeDescriptions = packedSnapshot.typeDescriptions;
			result.managedHeap = packedSnapshot.managedHeap;

			var combined = new ThingInMemory[0].Concat(result.nativeObjects).Concat(result.gcHandles).Concat(result.managedObjects).Concat(result.staticFields).ToArray();
			var referencesLists = MakeTempLists(combined);
			var referencedByLists = MakeTempLists(combined);

			foreach (var connection in packedSnapshot.connections)
			{
				referencesLists[connection.from].Add(combined[connection.to]);
				referencedByLists[connection.to].Add(combined[connection.from]);
			}

			for (var i = 0; i != combined.Length; i++)
			{
				combined[i].references = referencesLists[i].ToArray();
				combined[i].referencedBy = referencedByLists[i].ToArray();
			}

			return null;
		}

		private static List<ThingInMemory>[] MakeTempLists(ThingInMemory[] combined)
		{
			var referencesLists = new List<ThingInMemory>[combined.Length];
			for (int i = 0; i != referencesLists.Length; i++)
				referencesLists[i] = new List<ThingInMemory>(4);
			return referencesLists;
		}

		private static StaticFields UnpackStaticFields(PackedCrawledMemorySnapshot packedSnapshot, PackedStaticFields psf)
		{
			return new StaticFields() {_typeDescription = packedSnapshot.typeDescriptions[psf.typeIndex]};
		}

		private static GCHandle UnpackGCHandle(PackedCrawledMemorySnapshot packedSnapshot, PackedGCHandle pgc)
		{
			return new GCHandle() {size = packedSnapshot.managedHeap.virtualMachineInformation.pointerSize };
		}

		private static ManagedObject UnpackManagedObject(PackedCrawledMemorySnapshot packedSnapshot, PackedManagedObject pm)
		{
			return new ManagedObject() {address = pm.address, size = pm.size, typeDescription = packedSnapshot.typeDescriptions[pm.typeIndex]};
		}

		private static NativeUnityEngineObject UnpackNativeUnityEngineObject(PackedCrawledMemorySnapshot packedCrawledMemorySnapshot, PackedNativeUnityEngineObject packedNativeUnityEngineObject)
		{
			return new NativeUnityEngineObject()
			{
				_instanceID = packedNativeUnityEngineObject.instanceID,
				_classID = packedNativeUnityEngineObject.classID,
				_className = packedCrawledMemorySnapshot.classIDNames[packedNativeUnityEngineObject.classID],
				_name = packedNativeUnityEngineObject.name
			};
		}
	}

	public class ThingInMemory
	{
		public int size;
		public ThingInMemory[] references;
		public ThingInMemory[] referencedBy;
	}

	public class ManagedObject : ThingInMemory
	{
		public UInt64 address;
		public TypeDescription typeDescription;
	}

	public class NativeUnityEngineObject : ThingInMemory
	{
		internal int _instanceID;
		internal int _classID;
		internal string _className;
		internal string _name;

		public int instanceID
		{
			get { return _instanceID; }
		}

		public int classId
		{
			get { return _classID; }
		}

		public string className
		{
			get { return _className; }
		}

		public string name
		{
			get { return _name; }
		}
	}

	public class GCHandle : ThingInMemory
	{
	}

	public class StaticFields : ThingInMemory
	{
		internal TypeDescription _typeDescription;

		public TypeDescription typeDescription
		{
			get { return _typeDescription; }
		}
	}
}