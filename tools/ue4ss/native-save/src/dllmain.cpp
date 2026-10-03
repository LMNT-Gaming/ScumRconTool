#include <Windows.h>
#include <MinHook.h>
#include <array>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace {
constexpr std::uintptr_t kLoadRva = 0x1AA8970;
constexpr std::uintptr_t kSaveRva = 0x1AB1680;
constexpr std::array<std::uint8_t, 40> kLoadBytes{
    0x48,0x8B,0xC4,0x48,0x89,0x58,0x20,0x55,0x48,0x8B,0xEC,0x48,0x81,0xEC,0x80,0x00,
    0x00,0x00,0x48,0x89,0x70,0x08,0x49,0x8B,0xF0,0x48,0x89,0x78,0x10,0x48,0x8B,0xF9,
    0x4C,0x89,0x70,0x18,0x48,0x8B,0xDA,0x48 };
constexpr std::array<std::uint8_t, 36> kSaveBytes{
    0x48,0x8B,0xC4,0x55,0x48,0x8B,0xEC,0x48,0x83,0xEC,0x70,0x48,0x89,0x58,0x08,0x48,
    0x8B,0xDA,0x48,0x89,0x70,0x10,0x49,0x8B,0xF0,0x48,0x89,0x78,0x18,0x48,0x8B,0xF9,
    0x48,0x85,0xC9,0x74 };

enum class Type { I32, F32 };
struct Field { const char* name; std::size_t offset; Type type; };
constexpr std::array<Field, 75> kFields{{
 {"highest_positive_fame_points",0x08,Type::F32},{"doors_claimed",0x0C,Type::I32},{"animals_killed",0x10,Type::I32},{"minutes_survived",0x14,Type::F32},{"kills",0x18,Type::I32},
 {"deaths",0x1C,Type::I32},{"locks_picked",0x20,Type::I32},{"puppets_killed",0x24,Type::I32},{"guns_crafted",0x28,Type::I32},{"bullets_crafted",0x30,Type::I32},
 {"arrows_crafted",0x34,Type::I32},{"clothing_crafted",0x38,Type::I32},{"longest_kill_distance",0x3C,Type::F32},{"melee_kills",0x40,Type::I32},{"archery_kills",0x44,Type::I32},
 {"players_knocked_out",0x48,Type::I32},{"total_defecations",0x4C,Type::I32},{"total_urinations",0x50,Type::I32},{"lights_fired",0x54,Type::I32},{"containers_looted",0x58,Type::I32},
 {"items_put_into_containers",0x5C,Type::I32},{"deaths_by_prisoners",0x60,Type::I32},{"animals_skinned",0x64,Type::I32},{"food_eaten",0x68,Type::F32},{"distance_travelled_by_foot",0x6C,Type::F32},
 {"wounds_patched",0x70,Type::I32},{"items_picked_up",0x74,Type::I32},{"liquid_drank",0x78,Type::F32},{"teeth_lost",0x7C,Type::I32},{"total_calories_intake",0x80,Type::I32},
 {"shots_fired",0x84,Type::I32},{"shots_hit",0x88,Type::I32},{"headshots",0x90,Type::I32},{"melee_weapon_swings",0x94,Type::I32},{"melee_weapon_hits",0x98,Type::I32},
 {"melee_weapons_crafted",0xA0,Type::I32},{"drone_kills",0xA4,Type::I32},{"sentry_kills",0xA8,Type::I32},{"prisoner_kills",0xAC,Type::I32},{"puppets_knocked_out",0xB0,Type::I32},
 {"diarrheas",0xB4,Type::I32},{"vomits",0xB8,Type::I32},{"distance_travelled_in_vehicle",0xBC,Type::F32},{"mushrooms_eaten",0xC0,Type::I32},{"highest_muscle_mass",0xC4,Type::F32},
 {"highest_fat",0xC8,Type::F32},{"heart_attacks",0xCC,Type::I32},{"overdose",0xD0,Type::I32},{"starvation",0xD4,Type::I32},{"highest_damage_taken",0xD8,Type::F32},
 {"highest_weight_carried",0xDC,Type::F32},{"lowest_negative_fame_points",0xE8,Type::F32},{"distance_travelled_swimming",0xEC,Type::F32},{"crows_killed",0xFC,Type::I32},{"seagulls_killed",0x100,Type::I32},
 {"horses_killed",0x104,Type::I32},{"boars_killed",0x108,Type::I32},{"bears_killed",0x10C,Type::I32},{"goats_killed",0x110,Type::I32},{"deers_killed",0x114,Type::I32},
 {"chickens_killed",0x118,Type::I32},{"rabbits_killed",0x11C,Type::I32},{"donkeys_killed",0x120,Type::I32},{"times_mauled_by_bear",0x128,Type::I32},{"longest_animal_kill_distance",0x12C,Type::F32},
 {"alcohol_drank",0x13C,Type::F32},{"foliage_cut",0x140,Type::I32},{"distance_travel_by_boat",0x170,Type::F32},{"distance_sailed",0x174,Type::F32},{"times_caught_by_shark",0x178,Type::I32},
 {"times_escaped_shark_bite",0x17C,Type::I32},{"wolves_killed",0x124,Type::I32},{"last_fame_point_award_consecutive_days",0x180,Type::I32},{"firearm_kills",0x184,Type::I32},{"bare_handed_kills",0x188,Type::I32}
}};

using StatsFn = std::uint64_t(__fastcall*)(void*, std::int64_t, void*);
StatsFn g_load{}, g_save{};
void* g_load_target{}; void* g_save_target{};
std::atomic_bool g_installed{false};
std::mutex g_log_mutex;

template<class T> bool read_at(const void* base, std::size_t offset, T& value) {
    __try { std::memcpy(&value, static_cast<const std::uint8_t*>(base)+offset, sizeof(T)); return true; }
    __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
}

void write_log(const std::string& message) {
    std::lock_guard lock(g_log_mutex);
    wchar_t path[MAX_PATH]{}; if(!GetModuleFileNameW(reinterpret_cast<HMODULE>(&__ImageBase),path,MAX_PATH)) return;
    wchar_t* slash=wcsrchr(path,L'\\'); if(!slash) return; *slash=L'\0';
    wchar_t log_path[MAX_PATH]{}; if(swprintf_s(log_path,L"%s\\SurvivalStatsNativeProbe.log",path)<0) return;
    FILE* file{}; if(_wfopen_s(&file,log_path,L"a, ccs=UTF-8")!=0||!file) return;
    SYSTEMTIME now{}; GetLocalTime(&now);
    fwprintf(file,L"%04u-%02u-%02uT%02u:%02u:%02u.%03u %hs\n",now.wYear,now.wMonth,now.wDay,now.wHour,now.wMinute,now.wSecond,now.wMilliseconds,message.c_str()); fclose(file);
}

void log_stats(const char* event, std::int64_t profile_id, const void* stats, std::uint64_t result) {
    std::int64_t structure_id{};
    if(!stats||!read_at(stats,0,structure_id)){write_log(std::string(event)+" profile_id="+std::to_string(profile_id)+" stats_read=failed");return;}
    std::string line=std::string(event)+" thread="+std::to_string(GetCurrentThreadId())+" arg_profile_id="+std::to_string(profile_id)+" struct_profile_id="+std::to_string(structure_id)+" result="+std::to_string(result);
    char number[64]{};
    for(const auto& field:kFields){line+=' ';line+=field.name;line+='=';
        if(field.type==Type::F32){float value{};if(!read_at(stats,field.offset,value)){line+="<read_failed>";continue;}snprintf(number,sizeof(number),"%.9g",value);}
        else{std::int32_t value{};if(!read_at(stats,field.offset,value)){line+="<read_failed>";continue;}snprintf(number,sizeof(number),"%d",value);}line+=number;}
    write_log(line);
}

std::uint64_t __fastcall on_load(void* db,std::int64_t id,void* stats){auto result=g_load(db,id,stats);log_stats("LOAD_STATS",id,stats,result);return result;}
std::uint64_t __fastcall on_save(void* db,std::int64_t id,void* stats){auto result=g_save(db,id,stats);log_stats("SAVE_STATS",id,stats,result);return result;}

bool install(){
    if(g_installed.exchange(true)){write_log("INSTALL already_installed");return true;}
    auto module=reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
    g_load_target=reinterpret_cast<void*>(module+kLoadRva);g_save_target=reinterpret_cast<void*>(module+kSaveRva);
    if(std::memcmp(g_load_target,kLoadBytes.data(),kLoadBytes.size())!=0){write_log("INSTALL refused load_prologue_mismatch");g_installed=false;return false;}
    if(std::memcmp(g_save_target,kSaveBytes.data(),kSaveBytes.size())!=0){write_log("INSTALL refused save_prologue_mismatch");g_installed=false;return false;}
    auto status=MH_Initialize();if(status!=MH_OK&&status!=MH_ERROR_ALREADY_INITIALIZED){write_log("INSTALL refused minhook_init="+std::to_string(status));g_installed=false;return false;}
    status=MH_CreateHook(g_load_target,reinterpret_cast<void*>(&on_load),reinterpret_cast<void**>(&g_load));if(status!=MH_OK){write_log("INSTALL refused create_load="+std::to_string(status));g_installed=false;return false;}
    status=MH_CreateHook(g_save_target,reinterpret_cast<void*>(&on_save),reinterpret_cast<void**>(&g_save));if(status!=MH_OK){MH_RemoveHook(g_load_target);write_log("INSTALL refused create_save="+std::to_string(status));g_installed=false;return false;}
    status=MH_EnableHook(MH_ALL_HOOKS);if(status!=MH_OK){MH_RemoveHook(g_save_target);MH_RemoveHook(g_load_target);write_log("INSTALL refused enable="+std::to_string(status));g_installed=false;return false;}
    char line[512]{};snprintf(line,sizeof(line),"INSTALL success module=%p load_target=%p load_rva=0x%llX save_target=%p save_rva=0x%llX fields=%zu",reinterpret_cast<void*>(module),g_load_target,static_cast<unsigned long long>(kLoadRva),g_save_target,static_cast<unsigned long long>(kSaveRva),kFields.size());write_log(line);return true;
}
}

extern "C" __declspec(dllexport) int InstallSurvivalStatsNativeProbe(void*){install();return 0;}
BOOL APIENTRY DllMain(HMODULE module,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(module);return TRUE;}
