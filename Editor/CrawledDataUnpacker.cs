using System.Collections.Generic;
using System.Linq;

namespace UnityEditor.Profiler.Memory
{
	class CrawlDataUnpacker
	{
		public static CrawledMemorySnapshot Unpack(PackedCrawledMemorySnapshot packedSnapshot)
		{
			var result = new CrawledMemorySnapshot
			{
				nativeObjects = packedSnapshot.nativeObjects.Select(packedNativeUnityEngineObject => UnpackNativeUnityEngineObject(packedSnapshot, packedNativeUnityEngineObject)).ToArray(),
				managedObjects = packedSnapshot.managedObjects.Select(pm => UnpackManagedObject(packedSnapshot, pm)).ToArray(),
				gcHandles = packedSnapshot.gcHandles.Select(pgc => UnpackGCHandle(packedSnapshot, pgc)).ToArray(),
				staticFields = packedSnapshot.packedStaticFields.Select(psf => UnpackStaticFields(packedSnapshot, psf)).ToArray(),
				typeDescriptions = packedSnapshot.typeDescriptions,
				managedHeap = packedSnapshot.managedHeap
			};

			var combined = new ThingInMemory[0].Concat(result.nativeObjects).Concat(result.gcHandles).Concat(result.managedObjects).Concat(result.staticFields).ToArray();
			result.allObjects = combined;

			var referencesLists = MakeTempLists(combined);
			var referencedByLists = MakeTempLists(combined);

			foreach (var connection in packedSnapshot.connections)
			{
				referencesLists[connection.@from].Add(combined[connection.to]);
				referencedByLists[connection.to].Add(combined[connection.@from]);
			}

			for (var i = 0; i != combined.Length; i++)
			{
				combined[i].references = referencesLists[i].ToArray();
				combined[i].referencedBy = referencedByLists[i].ToArray();
			}

			return null;
		}

		static List<ThingInMemory>[] MakeTempLists(ThingInMemory[] combined)
		{
			var referencesLists = new List<ThingInMemory>[combined.Length];
			for (int i = 0; i != referencesLists.Length; i++)
				referencesLists[i] = new List<ThingInMemory>(4);
			return referencesLists;
		}

		static StaticFields UnpackStaticFields(PackedCrawledMemorySnapshot packedSnapshot, PackedStaticFields psf)
		{
			var typeDescription = packedSnapshot.typeDescriptions[psf.typeIndex];
			return new StaticFields()
			{
				typeDescription = typeDescription,
				caption = "static fields of "+typeDescription.name,
			};
		}

		static GCHandle UnpackGCHandle(PackedCrawledMemorySnapshot packedSnapshot, PackedGCHandle pgc)
		{
			return new GCHandle() { size = packedSnapshot.managedHeap.virtualMachineInformation.pointerSize, caption = "gchandle" };
		}

		static ManagedObject UnpackManagedObject(PackedCrawledMemorySnapshot packedCrawledMemorySnapshot, PackedManagedObject pm)
		{
			var typeDescription = packedCrawledMemorySnapshot.typeDescriptions[pm.typeIndex];
			return new ManagedObject() { address = pm.address, size = pm.size, typeDescription = typeDescription, caption = typeDescription.name };
		}

		static NativeUnityEngineObject UnpackNativeUnityEngineObject(PackedCrawledMemorySnapshot packedSnapshot, PackedNativeUnityEngineObject packedNativeUnityEngineObject)
		{
			return new NativeUnityEngineObject()
			{
				instanceID = packedNativeUnityEngineObject.instanceID,
				classID = packedNativeUnityEngineObject.classID,
				className = packedSnapshot.classIDNames[packedNativeUnityEngineObject.classID],
				name = packedNativeUnityEngineObject.name,
				caption = packedNativeUnityEngineObject.name + "(className)"
			};
		}
	}
}