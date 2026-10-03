# SCUM SurvivalStatsProbe

This read-only UE4SS Lua probe searches the running dedicated server for
`SurvivalStatsHandler` instances. For each new instance it logs reflected
properties, functions, class hierarchy, and outer object. Once per minute it
logs fields whose names look related to survival statistics or player identity.

Expected log marker:

```text
[SurvivalStatsProbe]
```

The deployed mod belongs in:

```text
ue4ss/Mods/SurvivalStatsProbe/Scripts/main.lua
```

and must be enabled in `ue4ss/Mods/mods.txt`:

```text
SurvivalStatsProbe : 1
```

The probe never writes an Unreal property and exposes no network endpoint.
