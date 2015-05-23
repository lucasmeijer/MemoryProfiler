﻿using System;

namespace UnityEditor.Profiler.Memory
{
	//this is the highest level dataformat. it can be unpacked from the PackedCrawledMemorySnapshot, which contains all the interesting information we want. The Packed format
	//however is designed to be serializable and relatively storage compact.  This dataformat is designed to give a nice c# api experience. so while the packed version uses typeIndex,
	//this version has TypeReferences,  and also uses references to ThingInObject, instead of the more obscure object indexing pattern that the packed format uses.
	public class CrawledMemorySnapshot
	{
		public NativeUnityEngineObject[] nativeObjects;
		public GCHandle[] gcHandles;
		public ManagedObject[] managedObjects;
		public StaticFields[] staticFields;

		//contains concatenation of nativeObjects, gchandles, managedobjects and staticfields
		public ThingInMemory[] allObjects; 

		public ManagedHeap managedHeap;
		public TypeDescription[] typeDescriptions;
		public string[] classIDNames;
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
		public int instanceID;
		public int classID;
		public string className;
		public string name;
	}

	public class GCHandle : ThingInMemory
	{
	}

	public class StaticFields : ThingInMemory
	{
		public TypeDescription typeDescription;
	}
}