Here is the translation with the original Markdown formatting preserved:

# Nuclear 14 / Nuclear Winter: Ashfall

**Nuclear Winter** is a english fork of **Misfits:Nuclear Wasteland**, and by that, directly another fork of **Nuclear 14**.

## About the Project

Nuclear 14 is the first Fallout fork for Space Station 14, utilizing:

* Assets from various Fallout13 (F13/SS13) builds. Desert Rose 2, Lone Star, etc.
* Unique materials created by the community
* A highly modular system from the upstream Einstein Engines repository

The theme and locations differ from classic F13, offering players a new experience. The codebase is licensed under AGPLv3, which allows for free use and development of the project.

## Features of the Russian Version

Nuclear Winter stills in development, and curently we are actively working in the map. It is a huge project.

## Links

* [Discord](No discord for now) (official community)
* Game servers: via launcher (`tbd`) or in Discord

## Building

### Requirements

* Git
* .NET SDK 10.0.101

### Windows

1. Clone the repository:
```sh
git clone https://github.com/Misfit-Sanctuary/nuclear-14.git

```


2. Initialize submodules:
```sh
git submodule update --init --recursive

```


3. Build the project:
```sh
Scripts/bat/buildAllDebug.bat

```


4. Run client and server:
```sh
Scripts/bat/runQuickAll.bat

```


5. Connect to localhost via the client

### Linux

Similar to Windows, but use the `.sh` scripts:

```sh
Scripts/sh/buildAllDebug.sh
Scripts/sh/runQuickAll.sh

```

### MacOS

Theoretically similar to Linux, but has not been tested

## License

Detailed information about code and asset licensing is available in [LEGAL.md](https://www.google.com/search?q=./LEGAL.md). Key provisions:

* Code: AGPLv3
* Assets: individual licenses (check meta.json)
* Copyright compliance is mandatory

---

