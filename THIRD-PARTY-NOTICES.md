# Third-party software in the Windows distribution

Ping Watchdog's proprietary license covers Joseph Luker's code. Third-party components retain their own terms. The Windows package includes a ThirdPartyNotices folder. Its inventory identifies the versions resolved by that build and the license and notice files supplied with them. The inventory includes build dependencies for reference.

| Component | Distribution terms and source |
| --- | --- |
| Velopack updater and packaging tools | The pinned source release uses the MIT license. Preserve the copyright and permission notice. Upstream also identifies separate terms for vendor tools. [License](docs/licenses/Velopack-MIT.txt) · [Upstream vendor list](docs/licenses/Velopack-vendor-notices.md) |
| Microsoft Windows App SDK | Section 3 of the binary package terms covers files placed alongside the application by the NuGet package, including self-contained deployments. Redistribution carries requirements for end-user terms, notice preservation and use of trademarks. [Package license](docs/licenses/WindowsAppSDK-2.4.0.txt) |
| .NET and Windows Desktop runtime | The self-contained Windows build includes Microsoft runtime packs. Their package terms and upstream third-party notices accompany the build in ThirdPartyNotices. |

The build reads NuGet metadata from the restored packages and runtime packs used for Windows x64. It keeps supplied license files, notices and copyright information alongside the inventory. An entry in the inventory records a dependency in the build graph. It does not establish that every file in that package is installed on a user's machine.
