# Ready or Not Shields

SPT 4.1.6 · Version 0.5.0

## Build

Use the .NET 10 SDK and the .NET Framework 4.7.2 targeting pack. In PowerShell, point SPTRoot at your SPT installation and build both projects.

```powershell
$env:SPTRoot = 'D:\Games\SPT'
dotnet build Client -c Release
dotnet build Server -c Release
```

Open the Unity folder with Unity 2022.3.43f1. Create the Build directory beside Unity before running the editor build methods ShieldBuild.Run and ClearViewportBuild.Run. The baked hand and shield motion is embedded from Client/ShieldMotion.bin. The optional Python preparation scripts use RON_EXPORT for extracted input files.

## Install

Copy Hawks.RonShields.Client.dll and the two hawks.ron.*.bundle files to BepInEx/plugins/Hawks-RoNShields/. Copy Hawks.RonShields.Server.dll to SPT_Runtime/user/mods/Hawks-RoNShields/.
