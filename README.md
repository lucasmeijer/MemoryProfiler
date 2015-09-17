# MemoryProfiler

Unity 5.3 has a new very lowlevel memory profiler API. It can tell you which objects got blamed for how much
c++ memory allocations. On IL2CPP platforms, it will also give you a dump of the entire c# heap, as well as c# type descriptions.

This API is too low level for most people to benefit from. The intention is to write a much nicer UI window ontop of this API that will actually be readily useful for many users, in helping them figure out which objects are loaded,  which objects take a lot of memory,  and most important, _why_ that object is in memory. This repository is that nicer UI window, very much in progress of being built.

A common pattern of "memory leaks" that we see, is if you have a c# class with a static field, that contains a list, that has an object, which has a reference to a big Texture2D. This Texture2D will never be unloaded, because it is still reachable from C#. Figuring out this is the case is not very straightforward. This new memory profiler window will make it straight forward, and if you select the big Texture2D, it will show you a backtrace of references, all the way to the static c# field, (including the classname) that is causing this Texture2D to be loaded. (this only works on il2cpp platforms, as we need the whole il2cpp heap to do this backtracking)

We have taken the, for us new, approach of shipping the low level API in the Unity product itself, and already opensourcing this memory profiler window long before it is "ready enough to be included in unity proper".  We'll develop this window as an opensource project. Contributions are welcomed. At some point we'll feel that the feature is "good enough", at which point we'll most likely ship it out of the box with Unity.

Today, the UI part of this feature is still very rough. We decided to opensource it anyway, as when your project is in a state of "we need to use less memory, what do we do now", you probably care alot more about finding your memory leaks today,  then finding them 6 months from now with nicer buttons and user experience.

# Usage:
- Make sure you're running Unity5.3a4 or later.
- Build an il2cpp project & run it (any il2cpp platform should be fine).
- Open the user project in this repository.
- Open the normal profiler window.
- Connect it to the player as you would normally profile.
- Open the memory profiler window (Window->MemoryProfilerWindow)
- Click "SnapShot".
