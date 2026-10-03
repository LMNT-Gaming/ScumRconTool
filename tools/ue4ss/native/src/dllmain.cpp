#include <Windows.h>
#include <MinHook.h>

#include <array>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <mutex>

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace
{
constexpr std::uintptr_t kLoadSurvivalStatsRva = 0x1AA8970;
constexpr std::array<std::uint8_t, 40> kExpectedPrologue{
    0x48, 0x8B, 0xC4, 0x48, 0x89, 0x58, 0x20, 0x55,
    0x48, 0x8B, 0xEC, 0x48, 0x81, 0xEC, 0x80, 0x00,
    0x00, 0x00, 0x48, 0x89, 0x70, 0x08, 0x49, 0x8B,
    0xF0, 0x48, 0x89, 0x78, 0x10, 0x48, 0x8B, 0xF9,
    0x4C, 0x89, 0x70, 0x18, 0x48, 0x8B, 0xDA, 0x48,
};

using LoadSurvivalStatsFn = std::uint64_t(__fastcall*)(void*, std::int64_t, void*);
LoadSurvivalStatsFn g_original{};
void* g_target{};
std::atomic_bool g_installed{false};
std::mutex g_log_mutex;

void write_log(const char* format, ...)
{
    std::lock_guard lock(g_log_mutex);
    wchar_t module_path[MAX_PATH]{};
    if (!GetModuleFileNameW(reinterpret_cast<HMODULE>(&__ImageBase), module_path, MAX_PATH)) return;
    wchar_t* slash = wcsrchr(module_path, L'\\');
    if (!slash) return;
    *slash = L'\0';

    wchar_t log_path[MAX_PATH]{};
    if (swprintf_s(log_path, L"%s\\SurvivalStatsNativeProbe.log", module_path) < 0) return;
    FILE* file{};
    if (_wfopen_s(&file, log_path, L"a, ccs=UTF-8") != 0 || !file) return;

    SYSTEMTIME now{};
    GetLocalTime(&now);
    fwprintf(file, L"%04u-%02u-%02uT%02u:%02u:%02u.%03u ", now.wYear, now.wMonth,
             now.wDay, now.wHour, now.wMinute, now.wSecond, now.wMilliseconds);
    char buffer[2048]{};
    va_list arguments;
    va_start(arguments, format);
    vsnprintf_s(buffer, sizeof(buffer), _TRUNCATE, format, arguments);
    va_end(arguments);
    fwprintf(file, L"%hs\n", buffer);
    fclose(file);
}

template <typename T>
bool guarded_read(const void* base, std::size_t offset, T& value)
{
    __try { std::memcpy(&value, static_cast<const std::uint8_t*>(base) + offset, sizeof(T)); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

std::uint64_t __fastcall hooked_load_survival_stats(void* database_context,
                                                    std::int64_t user_profile_id,
                                                    void* output)
{
    const std::uint64_t result = g_original(database_context, user_profile_id, output);
    if (!output)
    {
        write_log("CALL thread=%lu profile_id=%lld result=%llu output=null", GetCurrentThreadId(),
                  static_cast<long long>(user_profile_id), static_cast<unsigned long long>(result));
        return result;
    }

    std::int64_t structure_profile_id{};
    float highest_positive_fame_points{}, minutes_survived{};
    std::int32_t doors_claimed{}, animals_killed{}, kills{}, deaths{}, locks_picked{},
                 puppets_killed{}, guns_crafted{};
    const bool readable = guarded_read(output, 0x00, structure_profile_id) &&
        guarded_read(output, 0x08, highest_positive_fame_points) && guarded_read(output, 0x0C, doors_claimed) &&
        guarded_read(output, 0x10, animals_killed) && guarded_read(output, 0x14, minutes_survived) &&
        guarded_read(output, 0x18, kills) && guarded_read(output, 0x1C, deaths) &&
        guarded_read(output, 0x20, locks_picked) && guarded_read(output, 0x24, puppets_killed) &&
        guarded_read(output, 0x28, guns_crafted);

    if (!readable)
    {
        write_log("CALL thread=%lu profile_id=%lld result=%llu output=%p read=failed", GetCurrentThreadId(),
                  static_cast<long long>(user_profile_id), static_cast<unsigned long long>(result), output);
        return result;
    }

    write_log("STATS thread=%lu arg_profile_id=%lld struct_profile_id=%lld result=%llu "
              "minutes_survived=%.6g highest_positive_fame_points=%.6g doors_claimed=%d "
              "animals_killed=%d kills=%d deaths=%d locks_picked=%d puppets_killed=%d guns_crafted=%d",
              GetCurrentThreadId(), static_cast<long long>(user_profile_id),
              static_cast<long long>(structure_profile_id), static_cast<unsigned long long>(result),
              minutes_survived, highest_positive_fame_points, doors_claimed, animals_killed,
              kills, deaths, locks_picked, puppets_killed, guns_crafted);
    return result;
}

bool install_probe()
{
    if (g_installed.exchange(true)) { write_log("INSTALL already_installed"); return true; }
    const auto module = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
    if (!module) { write_log("INSTALL refused reason=no_main_module"); g_installed = false; return false; }
    g_target = reinterpret_cast<void*>(module + kLoadSurvivalStatsRva);
    if (std::memcmp(g_target, kExpectedPrologue.data(), kExpectedPrologue.size()) != 0)
    {
        write_log("INSTALL refused reason=prologue_mismatch module=%p target=%p rva=0x%llX",
                  reinterpret_cast<void*>(module), g_target, static_cast<unsigned long long>(kLoadSurvivalStatsRva));
        g_installed = false;
        return false;
    }

    const MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED)
    { write_log("INSTALL refused reason=minhook_initialize status=%d", init); g_installed = false; return false; }
    const MH_STATUS created = MH_CreateHook(g_target, reinterpret_cast<void*>(&hooked_load_survival_stats),
                                             reinterpret_cast<void**>(&g_original));
    if (created != MH_OK)
    { write_log("INSTALL refused reason=create_hook status=%d", created); g_installed = false; return false; }
    const MH_STATUS enabled = MH_EnableHook(g_target);
    if (enabled != MH_OK)
    { MH_RemoveHook(g_target); write_log("INSTALL refused reason=enable_hook status=%d", enabled); g_installed = false; return false; }
    write_log("INSTALL success module=%p target=%p rva=0x%llX", reinterpret_cast<void*>(module), g_target,
              static_cast<unsigned long long>(kLoadSurvivalStatsRva));
    return true;
}

void uninstall_probe()
{
    if (!g_installed.exchange(false)) return;
    if (g_target) { MH_DisableHook(g_target); MH_RemoveHook(g_target); }
    MH_Uninitialize();
}
} // namespace

extern "C" __declspec(dllexport) int InstallSurvivalStatsNativeProbe(void*)
{
    install_probe();
    return 0;
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(module);
    // Do not run locking/file-I/O cleanup under the Windows loader lock.
    return TRUE;
}
