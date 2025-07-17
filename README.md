# strei

![logo](logo.png)

strei is a trey audio player designed specifically for continuous, background ambient sounds with automatic loop. It uses LibVLCSharp.

With strei, no longer opening up foreground sounds and movies interrupt background ones.

Upon startup, strei sits in your trey ready to load a sound file. An initial media directory can be specified. Playback can be paused and resumed.

strei uses about 11 MB by itself, plus a fraction of the size of the media file it is currently playing for its buffer. Memory is recovered after each new sound is loaded.

It is very small at around 1 MB.

Multiple instances of strei can be ran simultaneously to superimpose background sounds.

### Requirements

strei is currently compatible with Windows only.

It does not bundle and requires an installation of VLC.

strei requires .NET Desktop Runtime 7 or greater, which can be downloaded from https://dotnet.microsoft.com/download.

### Installation

strei is portable and doesn't require installation.

Copy your files from a release to a directory and run `strei.exe`.

Then, using the trey icon, select your VLC installation directory, and, if desired, a media directory.

Then, you're ready to load a sound.

### Building

Use Visual Studio 2022 or `dotnet build` to build.