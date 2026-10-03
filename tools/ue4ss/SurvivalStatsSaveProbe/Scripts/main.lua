local MOD_TAG = "[SurvivalStatsSaveProbe]"
local script_path = debug.getinfo(1, "S").source:sub(2)
local dll_path = script_path:gsub("[\\/]Scripts[\\/]main.lua$", "\\dlls\\SurvivalStatsNativeProbe.dll")
local loader, load_error = package.loadlib(dll_path, "InstallSurvivalStatsNativeProbe")
if not loader then
    print(string.format("%s DLL load failed: %s\n", MOD_TAG, tostring(load_error)))
else
    local ok, call_error = pcall(loader)
    if ok then print(string.format("%s load/save hook install requested\n", MOD_TAG))
    else print(string.format("%s install call failed: %s\n", MOD_TAG, tostring(call_error))) end
end
