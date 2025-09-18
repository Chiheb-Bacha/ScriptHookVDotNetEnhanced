Script Hook V .Net Enhanced (SHVDNE)
============================

[![Downloads](https://img.shields.io/github/downloads/Chiheb-Bacha/scripthookvdotnetenhanced/total?label=Downloads)](https://github.com/Chiheb-Bacha/scripthookvdotnetenhanced/releases)
[![Latest Release](https://img.shields.io/github/v/release/Chiheb-Bacha/scripthookvdotnetenhanced?include_prereleases&label=Version)](https://github.com/Chiheb-Bacha/scripthookvdotnetenhanced/releases/latest)
[![License](https://img.shields.io/github/license/Chiheb-Bacha/scripthookvdotnetenhanced?color=%232A922A)](LICENSE.md)

This is an ASI plugin for Grand Theft Auto V Legacy & Enhanced editions, based on the C++ ScriptHook by Alexander Blade, which allows running scripts written in any .NET language in-game.

The original project ([SHVDN](https://github.com/scripthookvdotnet/scripthookvdotnet)), which only provided support for Grand Theft Auto V Legacy edition, was extended to further support the Enhanced edition.

SHVDNE allows scripts built for GTA5 Legacy edition to run as-is with the Enhanced Edition (granted that they do not rely on their own memory patterns). It can also be used as a drop-in replacement for SHVDN for the GTA5 Legacy edition.
This means script developers can target both editions with the same files.

The issues page should be primarily used for bug reports and focused enhancement ideas. Questions related to GTA V scripting in general, are better off on the [Discussions page](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/discussions) or in forums dedicated to this purpose.

## Requirements

* [C++ Script Hook V by Alexander Blade](http://www.dev-c.com/gtav/scripthookv/)
* [.NET Framework ≥ 4.8](https://dotnet.microsoft.com/en-us/download/dotnet-framework/thank-you/net48-web-installer)
* [Visual C++ Redistributable for Visual Studio 2019 x64](https://aka.ms/vs/17/release/vc_redist.x64.exe)

## Downloads
The stable builds can be downloaded from the [Releases](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases) page.
You need to use the ASI file and the DLL files for APIs in an archive of the same version as the internal structure can be changed without notice.  

* The default API version for raw scripts is changed from v2 to v3.
    * **For Users**: If you have raw scripts (`.cs` and `.vb` scripts) without an API version notation by file name, you will need to specify it. You can specify an API version by adding a dot and a version number right before the extension name (e.g. `Script.cs` to `Script.2.cs`).
* You should use the .ini file that comes from a release. SHVDNE does not add missing settings currently.
* Warning messages are added for scripts built against the v2 API, which is not as maintained as the v3 one and will not have any new features. This does not mean the v2 API will not be even receiving compatibility fixes for new game updates in the *near* future. These messages are printed to urge users to ask the script authors to migrate to the v3 API.
* Some scripts *may* not be working that rely on the main thread of the game process (for game logic).
    * This is because we had to use a dedicated thread other than the main thread to avoid using ScriptHookV's fiber, so users won't have crucial compatibility problems with RAGE Plugin Hook and C++ scripts that use try-catch blocks. Although we are still searching for how to have SHVDN tick in the main game thread by hooking a function in the game process, we have not found one.

## Installation
* Extract all the files in the root folder from the zip file into your game folder except for `README.txt` and the 2 folders.
    * The XML files in the `Docs` folder are provided solely as API documentation for script developers.
* When you update, **always make sure to update at least all the asi and the .dll files together! No support will be provided if you cherry-pick them and that causes problems!** The following files are the ones you must update together:
    * `ScriptHookVDotNet.asi`
    * `ScriptHookVDotNet2.dll`
    * `ScriptHookVDotNet3.dll`

## Contributing

You'll need Visual Studio 2019 or higher to open the project file and the [Script Hook V SDK](http://www.dev-c.com/gtav/scripthookv/) extracted into [/sdk](/sdk).

Any contributions to the project are welcomed, it's recommended to use GitHub [pull requests](https://help.github.com/articles/using-pull-requests/).

## License

ScriptHookVDotNetEnhanced is primarily distributed under the terms of the zlib license.
See [LICENSE](LICENSE.txt) and [COPYRIGHT](COPYRIGHT.md) for details.
