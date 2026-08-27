#include <windows.h>
#include <bcrypt.h>
#include <algorithm>
#include <atomic>
#include <cctype>
#include <cstdint>
#include <cmath>
#include <functional>
#include <iomanip>
#include <map>
#include <optional>
#include <sstream>
#include <stdexcept>
#include <string>
#include <thread>
#include <type_traits>
#include <unordered_set>
#include <vector>

namespace {
struct Il2CppDomain;
struct Il2CppAssembly;
struct Il2CppImage;
struct Il2CppClass;
struct FieldInfo;
struct MethodInfo;
struct Il2CppType;
struct Il2CppObject;
struct Il2CppString;

HMODULE selfModule{};
std::atomic_bool stopping{false};
HANDLE pipeHandle = INVALID_HANDLE_VALUE;

void nativeLog(const std::string& message) noexcept {
    wchar_t localAppData[MAX_PATH]{};
    if (!GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH)) return;
    const std::wstring root = std::wstring(localAppData) + L"\\ArenaDrafter";
    const std::wstring logs = root + L"\\logs";
    CreateDirectoryW(root.c_str(), nullptr);
    CreateDirectoryW(logs.c_str(), nullptr);
    const std::wstring path = logs + L"\\probe-" + std::to_wstring(GetCurrentProcessId()) + L".log";
    HANDLE file = CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    SYSTEMTIME time{};
    GetSystemTime(&time);
    std::ostringstream line;
    line << std::setfill('0') << std::setw(4) << time.wYear << '-' << std::setw(2) << time.wMonth << '-' << std::setw(2) << time.wDay
         << 'T' << std::setw(2) << time.wHour << ':' << std::setw(2) << time.wMinute << ':' << std::setw(2) << time.wSecond << '.' << std::setw(3) << time.wMilliseconds
         << "Z [PID " << GetCurrentProcessId() << "] " << message << "\r\n";
    const auto text = line.str();
    DWORD written = 0;
    WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr);
    CloseHandle(file);
}

void recordLiveArena(const std::string& event) {
    wchar_t localAppData[MAX_PATH]{};
    if (!GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH)) throw std::runtime_error("LOCALAPPDATA is unavailable.");
    const std::wstring root = std::wstring(localAppData) + L"\\ArenaDrafter";
    const std::wstring logs = root + L"\\logs";
    if (!CreateDirectoryW(root.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS)
        throw std::runtime_error("The research log directory could not be created.");
    if (!CreateDirectoryW(logs.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS)
        throw std::runtime_error("The research logs directory could not be created.");
    const std::wstring path = logs + L"\\live-arena-" + std::to_wstring(GetCurrentProcessId()) + L".jsonl";
    HANDLE file = CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) throw std::runtime_error("The Live Arena journal could not be opened.");
    SYSTEMTIME time{};
    GetSystemTime(&time);
    std::ostringstream line;
    line << "{\"utc\":\"" << std::setfill('0') << std::setw(4) << time.wYear << '-' << std::setw(2) << time.wMonth << '-' << std::setw(2) << time.wDay
         << 'T' << std::setw(2) << time.wHour << ':' << std::setw(2) << time.wMinute << ':' << std::setw(2) << time.wSecond << '.' << std::setw(3) << time.wMilliseconds
         << "Z\",\"event\":" << event << "}\r\n";
    const auto text = line.str();
    DWORD written = 0;
    const auto succeeded = WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr);
    CloseHandle(file);
    if (!succeeded || written != text.size()) throw std::runtime_error("The Live Arena journal write failed.");
}

class Api {
public:
    using DomainGet = Il2CppDomain* (*)();
    using DomainAssemblies = const Il2CppAssembly** (*)(const Il2CppDomain*, size_t*);
    using AssemblyImage = const Il2CppImage* (*)(const Il2CppAssembly*);
    using ClassFromName = Il2CppClass* (*)(const Il2CppImage*, const char*, const char*);
    using ClassFromType = Il2CppClass* (*)(const Il2CppType*);
    using ClassIsValueType = bool (*)(Il2CppClass*);
    using ClassParent = Il2CppClass* (*)(Il2CppClass*);
    using ClassField = FieldInfo* (*)(Il2CppClass*, const char*);
    using ClassMethod = const MethodInfo* (*)(Il2CppClass*, const char*, int);
    using ClassMethods = const MethodInfo* (*)(Il2CppClass*, void**);
    using ClassFields = FieldInfo* (*)(Il2CppClass*, void**);
    using ClassName = const char* (*)(Il2CppClass*);
    using ClassNamespace = const char* (*)(Il2CppClass*);
    using ClassElement = Il2CppClass* (*)(Il2CppClass*);
    using ObjectClass = Il2CppClass* (*)(Il2CppObject*);
    using FieldValue = void (*)(Il2CppObject*, FieldInfo*, void*);
    using FieldStaticValue = void (*)(FieldInfo*, void*);
    using FieldOffset = size_t (*)(FieldInfo*);
    using FieldParent = Il2CppClass* (*)(FieldInfo*);
    using FieldName = const char* (*)(FieldInfo*);
    using FieldType = const Il2CppType* (*)(FieldInfo*);
    using FieldFlags = uint32_t (*)(FieldInfo*);
    using TypeName = char* (*)(const Il2CppType*);
    using MethodName = const char* (*)(const MethodInfo*);
    using MethodParamCount = uint32_t (*)(const MethodInfo*);
    using MethodParam = const Il2CppType* (*)(const MethodInfo*, uint32_t);
    using MethodReturnType = const Il2CppType* (*)(const MethodInfo*);
    using Free = void (*)(void*);
    using RuntimeInvoke = Il2CppObject* (*)(const MethodInfo*, void*, void**, Il2CppObject**);
    using ObjectUnbox = void* (*)(Il2CppObject*);
    using ArrayLength = uintptr_t (*)(Il2CppObject*);
    using ArrayElementSize = uint32_t (*)(Il2CppClass*);
    using StringChars = const wchar_t* (*)(Il2CppString*);
    using StringLength = int32_t (*)(Il2CppString*);
    using ThreadAttach = void* (*)(Il2CppDomain*);

    DomainGet domainGet{};
    DomainAssemblies domainAssemblies{};
    AssemblyImage assemblyImage{};
    ClassFromName classFromName{};
    ClassFromType classFromType{};
    ClassIsValueType classIsValueType{};
    ClassParent classParent{};
    ClassField classField{};
    ClassMethod classMethod{};
    ClassMethods classMethods{};
    ClassFields classFields{};
    ClassName className{};
    ClassNamespace classNamespace{};
    ClassElement classElement{};
    ObjectClass objectClass{};
    FieldValue fieldValue{};
    FieldStaticValue fieldStaticValue{};
    FieldOffset fieldOffset{};
    FieldParent fieldParent{};
    FieldName fieldName{};
    FieldType fieldType{};
    FieldFlags fieldFlags{};
    TypeName typeName{};
    MethodName methodName{};
    MethodParamCount methodParamCount{};
    MethodParam methodParam{};
    MethodReturnType methodReturnType{};
    Free freeMemory{};
    RuntimeInvoke runtimeInvoke{};
    ObjectUnbox objectUnbox{};
    ArrayLength arrayLength{};
    ArrayElementSize arrayElementSize{};
    StringChars stringChars{};
    StringLength stringLength{};
    ThreadAttach threadAttach{};

    void load() {
        HMODULE module = GetModuleHandleW(L"GameAssembly.dll");
        if (!module) throw std::runtime_error("GameAssembly.dll is not loaded.");
        domainGet = symbol<DomainGet>(module, "il2cpp_domain_get");
        domainAssemblies = symbol<DomainAssemblies>(module, "il2cpp_domain_get_assemblies");
        assemblyImage = symbol<AssemblyImage>(module, "il2cpp_assembly_get_image");
        classFromName = symbol<ClassFromName>(module, "il2cpp_class_from_name");
        classFromType = symbol<ClassFromType>(module, "il2cpp_class_from_type");
        classIsValueType = symbol<ClassIsValueType>(module, "il2cpp_class_is_valuetype");
        classParent = symbol<ClassParent>(module, "il2cpp_class_get_parent");
        classField = symbol<ClassField>(module, "il2cpp_class_get_field_from_name");
        classMethod = symbol<ClassMethod>(module, "il2cpp_class_get_method_from_name");
        classMethods = symbol<ClassMethods>(module, "il2cpp_class_get_methods");
        classFields = symbol<ClassFields>(module, "il2cpp_class_get_fields");
        className = symbol<ClassName>(module, "il2cpp_class_get_name");
        classNamespace = symbol<ClassNamespace>(module, "il2cpp_class_get_namespace");
        classElement = symbol<ClassElement>(module, "il2cpp_class_get_element_class");
        objectClass = symbol<ObjectClass>(module, "il2cpp_object_get_class");
        fieldValue = symbol<FieldValue>(module, "il2cpp_field_get_value");
        fieldStaticValue = symbol<FieldStaticValue>(module, "il2cpp_field_static_get_value");
        fieldOffset = symbol<FieldOffset>(module, "il2cpp_field_get_offset");
        fieldParent = symbol<FieldParent>(module, "il2cpp_field_get_parent");
        fieldName = symbol<FieldName>(module, "il2cpp_field_get_name");
        fieldType = symbol<FieldType>(module, "il2cpp_field_get_type");
        fieldFlags = symbol<FieldFlags>(module, "il2cpp_field_get_flags");
        typeName = symbol<TypeName>(module, "il2cpp_type_get_name");
        methodName = symbol<MethodName>(module, "il2cpp_method_get_name");
        methodParamCount = symbol<MethodParamCount>(module, "il2cpp_method_get_param_count");
        methodParam = symbol<MethodParam>(module, "il2cpp_method_get_param");
        methodReturnType = symbol<MethodReturnType>(module, "il2cpp_method_get_return_type");
        freeMemory = symbol<Free>(module, "il2cpp_free");
        runtimeInvoke = symbol<RuntimeInvoke>(module, "il2cpp_runtime_invoke");
        objectUnbox = symbol<ObjectUnbox>(module, "il2cpp_object_unbox");
        arrayLength = symbol<ArrayLength>(module, "il2cpp_array_length");
        arrayElementSize = symbol<ArrayElementSize>(module, "il2cpp_array_element_size");
        stringChars = symbol<StringChars>(module, "il2cpp_string_chars");
        stringLength = symbol<StringLength>(module, "il2cpp_string_length");
        threadAttach = symbol<ThreadAttach>(module, "il2cpp_thread_attach");
    }

    Il2CppClass* findClass(const char* nameSpace, const char* name) const {
        size_t count = 0;
        const auto assemblies = domainAssemblies(domainGet(), &count);
        if (!assemblies || count == 0 || count > 1000) throw std::runtime_error("IL2CPP assembly list is invalid.");
        for (size_t index = 0; index < count; ++index) {
            if (auto* found = classFromName(assemblyImage(assemblies[index]), nameSpace, name)) return found;
        }
        throw std::runtime_error(std::string("Required IL2CPP class is missing: ") + nameSpace + "." + name);
    }

    FieldInfo* field(Il2CppClass* type, const char* name) const {
        for (auto* current = type; current; current = classParent(current))
            if (auto* found = classField(current, name)) return found;
        throw std::runtime_error(std::string("Required IL2CPP field is missing: ") + name);
    }

    const MethodInfo* method(Il2CppClass* type, const char* name, int parameterCount = 0) const {
        for (auto* current = type; current; current = classParent(current))
            if (auto* found = classMethod(current, name, parameterCount)) return found;
        throw std::runtime_error(std::string("Required IL2CPP method is missing: ") + name);
    }

private:
    template<typename T> static T symbol(HMODULE module, const char* name) {
        auto result = reinterpret_cast<T>(GetProcAddress(module, name));
        if (!result) throw std::runtime_error(std::string("Required IL2CPP export is missing: ") + name);
        return result;
    }
};

Api api;

bool readable(const void* pointer, size_t bytes = sizeof(void*)) {
    if (!pointer) return false;
    MEMORY_BASIC_INFORMATION info{};
    if (!VirtualQuery(pointer, &info, sizeof(info)) || info.State != MEM_COMMIT || (info.Protect & (PAGE_NOACCESS | PAGE_GUARD))) return false;
    const auto start = reinterpret_cast<uintptr_t>(pointer);
    const auto end = reinterpret_cast<uintptr_t>(info.BaseAddress) + info.RegionSize;
    return start <= end && bytes <= end - start;
}

void requireObject(Il2CppObject* object, const char* message) {
    if (!readable(object, 16)) throw std::runtime_error(message);
}

template<typename T> T fieldValue(Il2CppObject* object, const char* name) {
    requireObject(object, "An IL2CPP object pointer is invalid.");
    T value{};
    api.fieldValue(object, api.field(api.objectClass(object), name), &value);
    return value;
}

Il2CppObject* objectField(Il2CppObject* object, const char* name) {
    auto* result = fieldValue<Il2CppObject*>(object, name);
    requireObject(result, (std::string("Required object field is null: ") + name).c_str());
    return result;
}

template<typename T> T invokeValue(Il2CppObject* object, const char* name) {
    Il2CppObject* exception = nullptr;
    auto* boxed = api.runtimeInvoke(api.method(api.objectClass(object), name), object, nullptr, &exception);
    if (exception || !boxed) throw std::runtime_error(std::string("Read-only getter failed: ") + name);
    auto* value = static_cast<T*>(api.objectUnbox(boxed));
    if (!readable(value, sizeof(T))) throw std::runtime_error(std::string("Getter returned an invalid value: ") + name);
    return *value;
}

template<typename T> std::optional<T> invokeNullable(Il2CppObject* object, const char* name) {
    Il2CppObject* exception = nullptr;
    auto* boxed = api.runtimeInvoke(api.method(api.objectClass(object), name), object, nullptr, &exception);
    if (exception) throw std::runtime_error(std::string("Read-only getter failed: ") + name);
    if (!boxed) return std::nullopt;
    auto* value = static_cast<T*>(api.objectUnbox(boxed));
    if (!readable(value, sizeof(T))) throw std::runtime_error(std::string("Getter returned an invalid value: ") + name);
    return *value;
}

std::string utf8(Il2CppString* value) {
    if (!value) return {};
    requireObject(reinterpret_cast<Il2CppObject*>(value), "An IL2CPP string pointer is invalid.");
    const auto length = api.stringLength(value);
    if (length < 0 || length > 4096) throw std::runtime_error("An IL2CPP string length is invalid.");
    const auto* chars = api.stringChars(value);
    if (!readable(chars, static_cast<size_t>(length) * sizeof(wchar_t))) throw std::runtime_error("An IL2CPP string buffer is invalid.");
    if (length == 0) return {};
    const int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, chars, length, nullptr, 0, nullptr, nullptr);
    if (size <= 0) throw std::runtime_error("An IL2CPP string could not be converted to UTF-8.");
    std::string result(static_cast<size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, chars, length, result.data(), size, nullptr, nullptr);
    return result;
}

Il2CppObject* currentLocalizer() {
    auto* serviceLocator = api.findClass("Client.App.Services", "ServiceLocator");
    Il2CppObject* localizer = nullptr;
    api.fieldStaticValue(api.field(serviceLocator, "<Localizer>k__BackingField"), &localizer);
    requireObject(localizer, "RAID's localization service is not initialized.");
    return localizer;
}

std::string localizedText(Il2CppObject* textKey, Il2CppObject* localizer) {
    if (!textKey) return {};
    auto* key = fieldValue<Il2CppString*>(textKey, "Key");
    if (!key) return utf8(fieldValue<Il2CppString*>(textKey, "DefaultValue"));
    int32_t clientStorage = 0;
    void* arguments[] = {key, &clientStorage};
    Il2CppObject* exception = nullptr;
    auto* value = api.runtimeInvoke(api.method(api.objectClass(localizer), "Localize", 2), localizer, arguments, &exception);
    if (exception || !value) throw std::runtime_error("RAID failed to localize a catalog label.");
    const auto result = utf8(reinterpret_cast<Il2CppString*>(value));
    return result.empty() ? utf8(fieldValue<Il2CppString*>(textKey, "DefaultValue")) : result;
}

std::string jsonEscape(const std::string& value) {
    std::ostringstream output;
    for (const unsigned char character : value) {
        switch (character) {
            case '"': output << "\\\""; break;
            case '\\': output << "\\\\"; break;
            case '\b': output << "\\b"; break;
            case '\f': output << "\\f"; break;
            case '\n': output << "\\n"; break;
            case '\r': output << "\\r"; break;
            case '\t': output << "\\t"; break;
            default:
                if (character < 0x20) output << "\\u" << std::hex << std::setw(4) << std::setfill('0') << static_cast<int>(character) << std::dec;
                else output << character;
        }
    }
    return output.str();
}

void sendLine(const std::string& line) {
    if (pipeHandle == INVALID_HANDLE_VALUE) return;
    const std::string data = line + "\n";
    DWORD written = 0;
    if (!WriteFile(pipeHandle, data.data(), static_cast<DWORD>(data.size()), &written, nullptr) || written != data.size())
        throw std::runtime_error("The named pipe write failed.");
}

void sendError(const char* code, const std::string& message) {
    nativeLog(std::string("ERROR ") + code + ": " + message);
    try { sendLine("{\"protocol\":1,\"type\":\"error\",\"code\":\"" + jsonEscape(code) + "\",\"message\":\"" + jsonEscape(message) + "\"}"); }
    catch (...) {}
}

void sendAutomation(const char* state, const std::string& message) {
    nativeLog(std::string("Automation ") + state + ": " + message);
    sendLine("{\"protocol\":1,\"type\":\"automation\",\"state\":\"" + jsonEscape(state) + "\",\"message\":\"" + jsonEscape(message) + "\"}");
}

std::string readLine() {
    std::string line;
    char character{};
    DWORD read = 0;
    while (!stopping && ReadFile(pipeHandle, &character, 1, &read, nullptr) && read == 1) {
        if (character == '\n') return line;
        if (character != '\r') line.push_back(character);
        if (line.size() > 1024) throw std::runtime_error("A named pipe command is too long.");
    }
    throw std::runtime_error("The named pipe was closed.");
}

std::string sha256(const std::vector<uint8_t>& input) {
    BCRYPT_ALG_HANDLE algorithm{};
    BCRYPT_HASH_HANDLE hash{};
    DWORD objectSize = 0, resultSize = 0;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) != 0 ||
        BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&objectSize), sizeof(objectSize), &resultSize, 0) != 0)
        throw std::runtime_error("SHA-256 initialization failed.");
    std::vector<uint8_t> object(objectSize), digest(32);
    if (BCryptCreateHash(algorithm, &hash, object.data(), objectSize, nullptr, 0, 0) != 0 ||
        BCryptHashData(hash, const_cast<PUCHAR>(input.data()), static_cast<ULONG>(input.size()), 0) != 0 ||
        BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0) != 0) {
        if (hash) BCryptDestroyHash(hash);
        BCryptCloseAlgorithmProvider(algorithm, 0);
        throw std::runtime_error("SHA-256 hashing failed.");
    }
    BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm, 0);
    std::ostringstream output;
    for (const auto byte : digest) output << std::uppercase << std::hex << std::setw(2) << std::setfill('0') << static_cast<int>(byte);
    return output.str();
}

Il2CppObject* appModel() {
    auto* type = api.findClass("Client.Model", "AppModel");
    Il2CppObject* exception = nullptr;
    auto* instance = api.runtimeInvoke(api.method(type, "get_Instance"), nullptr, nullptr, &exception);
    if (exception || !instance) throw std::runtime_error("AppModel is not initialized.");
    requireObject(instance, "AppModel pointer is invalid.");
    return instance;
}

std::vector<Il2CppObject*> referenceList(Il2CppObject* list) {
    const auto size = fieldValue<int32_t>(list, "_size");
    auto* items = objectField(list, "_items");
    const auto length = api.arrayLength(items);
    if (size < 0 || size > 20000 || static_cast<uintptr_t>(size) > length) throw std::runtime_error("An IL2CPP list size is invalid.");
    auto** data = reinterpret_cast<Il2CppObject**>(reinterpret_cast<uint8_t*>(items) + 0x20);
    if (!readable(data, static_cast<size_t>(size) * sizeof(void*))) throw std::runtime_error("An IL2CPP list buffer is invalid.");
    return {data, data + size};
}

template<typename T> std::vector<T> valueList(Il2CppObject* list) {
    const auto size = fieldValue<int32_t>(list, "_size");
    auto* items = objectField(list, "_items");
    const auto length = api.arrayLength(items);
    if (size < 0 || size > 20000 || static_cast<uintptr_t>(size) > length) throw std::runtime_error("An IL2CPP value list size is invalid.");
    auto* data = reinterpret_cast<T*>(reinterpret_cast<uint8_t*>(items) + 0x20);
    if (!readable(data, static_cast<size_t>(size) * sizeof(T))) throw std::runtime_error("An IL2CPP value list buffer is invalid.");
    return {data, data + size};
}

template<typename T> std::vector<T> valueArray(Il2CppObject* array, uintptr_t maximum = 20000) {
    if (!array) return {};
    const auto length = api.arrayLength(array);
    if (length > maximum) throw std::runtime_error("An IL2CPP value array size is invalid.");
    auto* data = reinterpret_cast<T*>(reinterpret_cast<uint8_t*>(array) + 0x20);
    if (!readable(data, static_cast<size_t>(length) * sizeof(T))) throw std::runtime_error("An IL2CPP value array buffer is invalid.");
    return {data, data + length};
}

std::vector<Il2CppObject*> referenceArray(Il2CppObject* array, uintptr_t maximum = 20000) {
    if (!array) return {};
    const auto length = api.arrayLength(array);
    if (length > maximum) throw std::runtime_error("An IL2CPP reference array size is invalid (length "
        + std::to_string(length) + ", maximum " + std::to_string(maximum) + ").");
    auto** data = reinterpret_cast<Il2CppObject**>(reinterpret_cast<uint8_t*>(array) + 0x20);
    if (!readable(data, static_cast<size_t>(length) * sizeof(void*))) throw std::runtime_error("An IL2CPP reference array buffer is invalid.");
    return {data, data + length};
}

std::vector<Il2CppObject*> dictionaryValues(Il2CppObject* dictionary) {
    auto* entries = objectField(dictionary, "_entries");
    const auto length = api.arrayLength(entries);
    if (length > 20000) throw std::runtime_error("An IL2CPP dictionary capacity is invalid.");
    auto* arrayClass = api.objectClass(entries);
    auto* entryClass = api.classElement(arrayClass);
    const auto stride = api.arrayElementSize(arrayClass);
    auto* valueField = api.field(entryClass, "value");
    const auto valueOffset = api.fieldOffset(valueField);
    size_t arrayValueOffset = valueOffset;
    if (arrayValueOffset + sizeof(void*) > stride && valueOffset >= 16) arrayValueOffset -= 16;
    if (stride < sizeof(void*) || stride > 256 || arrayValueOffset + sizeof(void*) > stride)
        throw std::runtime_error("An IL2CPP dictionary entry layout is invalid.");
    auto* data = reinterpret_cast<uint8_t*>(entries) + 0x20;
    if (!readable(data, static_cast<size_t>(length) * stride)) throw std::runtime_error("An IL2CPP dictionary buffer is invalid.");
    std::vector<Il2CppObject*> values;
    std::unordered_set<Il2CppObject*> seen;
    for (uintptr_t index = 0; index < length; ++index) {
        auto* value = *reinterpret_cast<Il2CppObject**>(data + index * stride + arrayValueOffset);
        if (value && readable(value, 16) && seen.insert(value).second) values.push_back(value);
    }
    return values;
}

struct SkillDefinition {
    int typeId{};
    int target{-1};
    int cooldown{};
    std::string name;
    int slot{};
    int variant{};
    bool requiresTarget{};
};

bool requiresExplicitTarget(Il2CppObject* skill, int32_t target) {
    if (target < 1 || target > 11 || target == 5 || target == 6 || target == 9) return false;
    auto* effects = fieldValue<Il2CppObject*>(skill, "Effects");
    if (!effects) return false;
    for (auto* effect : referenceList(effects)) {
        if (!effect || fieldValue<bool>(effect, "IsEffectDescription")) continue;
        auto* targetParams = fieldValue<Il2CppObject*>(effect, "TargetParams");
        if (targetParams && fieldValue<int32_t>(targetParams, "TargetType") == 0) return true;
    }
    return false;
}

struct Definition {
    int baseId{};
    int ascension{};
    int rarity{};
    int affinity{};
    int faction{};
    std::string name;
    std::vector<SkillDefinition> skills;
};

std::map<int, Definition> definitions(Il2CppObject* app) {
    Il2CppObject* exception = nullptr;
    auto* staticData = api.runtimeInvoke(api.method(api.objectClass(app), "get_StaticData"), app, nullptr, &exception);
    if (exception || !staticData) throw std::runtime_error("StaticData is not initialized.");
    auto* skillData = objectField(staticData, "SkillData");
    auto* skillTypes = objectField(skillData, "SkillTypes");
    auto* localizer = currentLocalizer();
    std::map<int32_t, SkillDefinition> skillsById;
    struct NullableInt { bool hasValue{}; uint8_t padding[3]{}; int32_t value{}; };
    for (auto* skill : referenceList(skillTypes)) {
        if (!skill) continue;
        const auto typeId = fieldValue<int32_t>(skill, "Id");
        const auto group = fieldValue<int32_t>(skill, "Group");
        const auto cooldown = fieldValue<int32_t>(skill, "Cooldown");
        const auto target = fieldValue<NullableInt>(skill, "Targets");
        auto* name = fieldValue<Il2CppObject*>(skill, "Name");
        const auto text = localizedText(name, localizer);
        if (typeId <= 0 || group < 0 || group > 1 || cooldown < 0 || cooldown > 100 || (target.hasValue && (target.value < 0 || target.value > 11)))
            throw std::runtime_error("A SkillType value is outside the supported range.");
        if (group == 0 && !invokeValue<bool>(skill, "get_IsHiddenOnHud")) {
            const auto targetType = target.hasValue ? target.value : -1;
            skillsById.emplace(typeId, SkillDefinition{typeId, targetType, cooldown,
                text.empty() ? "Skill " + std::to_string(typeId) : text, 0, 0, requiresExplicitTarget(skill, targetType)});
        }
    }
    if (skillsById.empty()) throw std::runtime_error("The static active-skill catalog is empty.");
    auto* heroData = objectField(staticData, "HeroData");
    auto* heroTypes = objectField(heroData, "HeroTypes");
    std::map<int, Definition> result;
    for (auto* type : referenceList(heroTypes)) {
        if (!type) continue;
        requireObject(type, "A HeroType pointer is invalid.");
        const int id = fieldValue<int32_t>(type, "Id");
        if (id <= 0) continue;
        Definition definition;
        definition.baseId = invokeValue<int32_t>(type, "get_BaseId");
        definition.ascension = invokeValue<int32_t>(type, "get_AscendLevel");
        definition.rarity = fieldValue<int32_t>(type, "Rarity");
        definition.faction = fieldValue<int32_t>(type, "Fraction");
        auto* name = fieldValue<Il2CppObject*>(type, "Name");
        definition.name = localizedText(name, localizer);
        definition.affinity = invokeValue<int32_t>(type, "get_DefaultElement");
        const auto forms = referenceArray(fieldValue<Il2CppObject*>(type, "Forms"), 16);
        if (forms.empty()) throw std::runtime_error("A HeroType has no supported hero form.");
        const auto supportedFormCount = std::min<size_t>(forms.size(), 2);
        for (size_t formIndex = 0; formIndex < supportedFormCount; ++formIndex) {
            auto* form = forms[formIndex];
            if (!form) throw std::runtime_error("A HeroType contains an invalid hero form.");
            auto* skillIds = fieldValue<Il2CppObject*>(form, "SkillTypeIds");
            if (!skillIds) throw std::runtime_error("A hero form has no skill list.");
            int slot = 0;
            for (const auto skillTypeId : valueList<int32_t>(skillIds)) {
                const auto skill = skillsById.find(skillTypeId);
                if (skill == skillsById.end()) continue;
                if (slot > 11) throw std::runtime_error("A hero form exposes more active skill slots than supported.");
                auto value = skill->second;
                value.slot = slot++;
                value.variant = static_cast<int>(formIndex);
                if (std::none_of(definition.skills.begin(), definition.skills.end(), [&](const auto& existing) {
                    return existing.typeId == value.typeId && existing.variant == value.variant;
                })) definition.skills.push_back(std::move(value));
            }
        }
        if (definition.baseId <= 0 || definition.ascension < 0 || definition.ascension > 6 || definition.rarity < 1 || definition.rarity > 6 || definition.affinity < 1 || definition.affinity > 4)
            throw std::runtime_error("A HeroType value is outside the supported range.");
        result[id] = std::move(definition);
    }
    if (result.empty()) throw std::runtime_error("The static hero catalog is empty.");
    return result;
}

std::string catalogSnapshot(const std::map<int, Definition>& catalog) {
    std::map<int32_t, std::pair<int32_t, const Definition*>> byBaseId;
    for (const auto& [typeId, definition] : catalog) {
        if (typeId <= 0 || definition.baseId <= 0 || definition.name.empty()) continue;
        auto [entry, inserted] = byBaseId.try_emplace(definition.baseId, typeId, &definition);
        if (!inserted && definition.ascension > entry->second.second->ascension) entry->second = {typeId, &definition};
    }
    if (byBaseId.empty() || byBaseId.size() > 10000) throw std::runtime_error("The static champion catalog is outside the supported range.");
    std::ostringstream output;
    output << "{\"protocol\":1,\"type\":\"catalog\",\"champions\":[";
    bool first = true;
    for (const auto& [baseId, champion] : byBaseId) {
        if (!first) output << ',';
        first = false;
        const auto& definition = *champion.second;
        output << "{\"typeId\":" << champion.first << ",\"baseId\":" << baseId << ",\"name\":\"" << jsonEscape(definition.name)
               << "\",\"rarity\":" << definition.rarity << ",\"skills\":[";
        for (size_t index = 0; index < definition.skills.size(); ++index) {
            if (index) output << ',';
            const auto& skill = definition.skills[index];
            output << "{\"typeId\":" << skill.typeId << ",\"slot\":" << skill.slot << ",\"name\":\"" << jsonEscape(skill.name)
                   << "\",\"target\":" << skill.target << ",\"cooldown\":" << skill.cooldown << ",\"variant\":" << skill.variant
                   << ",\"requiresTarget\":" << (skill.requiresTarget ? "true" : "false") << '}';
        }
        output << "]}";
    }
    output << "]}";
    return output.str();
}

std::string snapshot(Il2CppObject* app, const std::map<int, Definition>& catalog, int64_t revision) {
    auto* wrapper = objectField(app, "_userWrapper");
    auto* heroes = objectField(wrapper, "Heroes");
    auto* heroData = objectField(heroes, "HeroData");
    auto* dictionary = objectField(heroData, "HeroById");
    auto heroObjects = dictionaryValues(dictionary);
    std::sort(heroObjects.begin(), heroObjects.end(), [](auto* left, auto* right) { return fieldValue<int32_t>(left, "Id") < fieldValue<int32_t>(right, "Id"); });

    std::ostringstream output;
    output << "{\"protocol\":1,\"type\":\"snapshot\",\"revision\":" << revision << ",\"champions\":[";
    bool first = true;
    std::unordered_set<int32_t> ids;
    for (auto* hero : heroObjects) {
        const auto id = fieldValue<int32_t>(hero, "Id");
        const auto typeId = fieldValue<int32_t>(hero, "TypeId");
        if (id <= 0 || typeId <= 0 || !ids.insert(id).second) throw std::runtime_error("A hero instance identifier is invalid or duplicated.");
        const auto found = catalog.find(typeId);
        if (found == catalog.end()) throw std::runtime_error("A hero instance references an unknown HeroType.");
        const auto& definition = found->second;
        const auto grade = fieldValue<int32_t>(hero, "Grade");
        const auto level = fieldValue<int32_t>(hero, "Level");
        const auto empowerment = fieldValue<int32_t>(hero, "EmpowerLevel");
        const auto marker = fieldValue<int32_t>(hero, "Marker");
        const auto locked = fieldValue<bool>(hero, "Locked");
        const auto inStorage = fieldValue<bool>(hero, "InStorage");
        const auto inBathhouse = fieldValue<bool>(hero, "InBathhouse");
        const auto validMarker = marker == 0 || marker == 1 || (marker >= 100 && marker <= 102) || (marker >= 200 && marker <= 203) || marker == 300 || marker == 301;
        if (grade < 1 || grade > 6 || level < 1 || level > 60 || empowerment < 0 || empowerment > 4 || !validMarker || (inStorage && inBathhouse))
            throw std::runtime_error("A hero instance value is outside the supported range.");
        int awakening = 0;
        if (auto* ascend = fieldValue<Il2CppObject*>(hero, "DoubleAscendData")) awakening = fieldValue<int32_t>(ascend, "Grade");
        if (awakening < 0 || awakening > 6) throw std::runtime_error("A hero awakening value is outside the supported range.");
        if (!first) output << ',';
        first = false;
        output << "{\"id\":" << id << ",\"typeId\":" << typeId << ",\"baseId\":" << definition.baseId
               << ",\"name\":\"" << jsonEscape(definition.name) << "\",\"grade\":" << grade
               << ",\"ascension\":" << definition.ascension << ",\"level\":" << level
               << ",\"empowerment\":" << empowerment << ",\"marker\":" << marker << ",\"locked\":" << (locked ? "true" : "false")
               << ",\"inStorage\":" << (inStorage ? "true" : "false") << ",\"inBathhouse\":" << (inBathhouse ? "true" : "false")
               << ",\"awakening\":" << awakening << ",\"rarity\":" << definition.rarity
               << ",\"affinity\":" << definition.affinity << ",\"faction\":" << definition.faction << '}';
    }
    output << "]}";
    return output.str();
}

Il2CppObject* currentBattleProcessor() {
    static auto* type = api.findClass("ECS.ViewModel.BattleView", "BattleViewContext");
    static const auto* getter = api.method(type, "get_Processor");
    Il2CppObject* exception = nullptr;
    auto* processor = api.runtimeInvoke(getter, nullptr, nullptr, &exception);
    return exception ? nullptr : processor;
}

Il2CppObject* visibleBattleHud();

int64_t fixedInteger(int64_t raw) {
    return raw / (int64_t{1} << 32);
}

std::string battleSnapshot(const std::map<int, Definition>& catalog, int64_t revision) {
    auto* processor = currentBattleProcessor();
    if (!processor) return "{\"protocol\":1,\"type\":\"battle\",\"revision\":" + std::to_string(revision) + ",\"active\":false,\"kind\":0,\"stageId\":0,\"round\":0,\"turn\":0,\"activeHeroId\":0,\"finished\":false,\"autoMode\":false,\"heroes\":[],\"hudVisible\":false,\"modeChangeAvailable\":false,\"skillSelectionAvailable\":false,\"hudSkillCount\":0,\"hudSkills\":[]}";

    auto* context = fieldValue<Il2CppObject*>(processor, "<Context>k__BackingField");
    if (!context) return "{\"protocol\":1,\"type\":\"battle\",\"revision\":" + std::to_string(revision) + ",\"active\":false,\"kind\":0,\"stageId\":0,\"round\":0,\"turn\":0,\"activeHeroId\":0,\"finished\":false,\"autoMode\":false,\"heroes\":[],\"hudVisible\":false,\"modeChangeAvailable\":false,\"skillSelectionAvailable\":false,\"hudSkillCount\":0,\"hudSkills\":[]}";
    auto* setup = fieldValue<Il2CppObject*>(context, "Setup");
    auto* state = fieldValue<Il2CppObject*>(context, "State");
    if (!setup || !state) throw std::runtime_error("The active battle context is incomplete.");

    const auto kind = fieldValue<int32_t>(setup, "KindId");
    const auto stageId = fieldValue<int32_t>(setup, "StageId");
    const auto round = fieldValue<int32_t>(state, "CurrentRound");
    const auto turn = fieldValue<int32_t>(state, "CurrentTurn");
    const auto finished = fieldValue<bool>(state, "BattleFinished");
    const auto autoMode = fieldValue<bool>(state, "IsAutoBattleMode");
    const auto playerFirst = fieldValue<bool>(state, "IsPlayerTeamFirst");
    auto* firstTeam = fieldValue<Il2CppObject*>(state, "FirstTeam");
    auto* secondTeam = fieldValue<Il2CppObject*>(state, "SecondTeam");
    auto* activeHero = fieldValue<Il2CppObject*>(state, "ActiveHero");
    if (kind < 1 || kind > 9 || stageId < 0 || round < 0 || round > 100 || turn < 0 || turn > 100000 || !firstTeam || !secondTeam)
        throw std::runtime_error("An active battle value is outside the supported range.");
    const auto activeHeroId = activeHero ? fieldValue<int32_t>(activeHero, "<Id>k__BackingField") + 1 : 0;

    std::ostringstream output;
    output << "{\"protocol\":1,\"type\":\"battle\",\"revision\":" << revision << ",\"active\":true,\"kind\":" << kind
           << ",\"stageId\":" << stageId << ",\"round\":" << round << ",\"turn\":" << turn
           << ",\"activeHeroId\":" << activeHeroId << ",\"finished\":" << (finished ? "true" : "false")
           << ",\"autoMode\":" << (autoMode ? "true" : "false") << ",\"heroes\":[";
    bool first = true;
    std::unordered_set<int32_t> ids;
    const std::pair<Il2CppObject*, const char*> teams[] = {{firstTeam, playerFirst ? "Ally" : "Enemy"}, {secondTeam, playerFirst ? "Enemy" : "Ally"}};
    for (const auto& [team, teamName] : teams) {
        auto* heroes = fieldValue<Il2CppObject*>(team, "Heroes");
        if (!heroes) throw std::runtime_error("An active battle team has no hero list.");
        for (auto* hero : referenceList(heroes)) {
            if (!hero) continue;
            const auto id = fieldValue<int32_t>(hero, "<Id>k__BackingField");
            const auto typeId = fieldValue<int32_t>(hero, "TypeId");
            const auto grade = fieldValue<int32_t>(hero, "Grade");
            const auto level = fieldValue<int32_t>(hero, "Level");
            const auto slot = fieldValue<int32_t>(hero, "SlotId");
            const auto health = fixedInteger(fieldValue<int64_t>(hero, "Health"));
            auto* stats = fieldValue<Il2CppObject*>(hero, "<Stats>k__BackingField");
            const auto maxHealth = stats ? fixedInteger(fieldValue<int64_t>(stats, "Health")) : health;
            const auto duplicate = !ids.insert(id).second;
            if (id < 0 || typeId <= 0 || duplicate || grade < 0 || grade > 6 || level < 0 || level > 1000 || slot < 0 || slot > 100 || health < 0 || maxHealth < 0) {
                nativeLog("Invalid battle hero: team=" + std::string(teamName) + ", id=" + std::to_string(id) + ", typeId=" + std::to_string(typeId)
                    + ", duplicate=" + (duplicate ? "true" : "false") + ", grade=" + std::to_string(grade) + ", level=" + std::to_string(level)
                    + ", slot=" + std::to_string(slot) + ", health=" + std::to_string(health) + ", maxHealth=" + std::to_string(maxHealth) + ".");
                throw std::runtime_error("A battle hero value is outside the supported range.");
            }
            const auto found = catalog.find(typeId);
            const auto name = found == catalog.end() || found->second.name.empty() ? "Hero " + std::to_string(typeId) : found->second.name;
            if (!first) output << ',';
            first = false;
            output << "{\"id\":" << id + 1 << ",\"typeId\":" << typeId << ",\"baseId\":" << (found == catalog.end() ? 0 : found->second.baseId)
                   << ",\"name\":\"" << jsonEscape(name) << "\",\"team\":\"" << teamName
                   << "\",\"level\":" << level << ",\"grade\":" << grade << ",\"slot\":" << slot << ",\"health\":" << health
                   << ",\"maxHealth\":" << maxHealth << ",\"dead\":" << (health == 0 ? "true" : "false") << ",\"skills\":[";
            bool firstSkill = true;
            int32_t skillSlot = 0;
            if (auto* skills = fieldValue<Il2CppObject*>(hero, "Skills")) {
                for (auto* skill : referenceList(skills)) {
                    if (!skill || !fieldValue<bool>(skill, "<IsHeroSkill>k__BackingField") || fieldValue<bool>(skill, "<IsHiddenSecretSkill>k__BackingField")) continue;
                    const auto currentSkillSlot = skillSlot++;
                    const auto skillTypeId = fieldValue<int32_t>(skill, "TypeId");
                    const auto cooldown = fieldValue<int32_t>(skill, "Cooldown");
                    const auto maxCooldown = fieldValue<int32_t>(skill, "MaxCooldown");
                    if (skillTypeId <= 0 || cooldown < 0 || cooldown > 100 || maxCooldown < 0 || maxCooldown > 100)
                        throw std::runtime_error("A battle skill value is outside the supported range.");
                    const SkillDefinition* staticSkill = nullptr;
                    if (found != catalog.end()) {
                        for (const auto& candidate : found->second.skills)
                            if (candidate.typeId == skillTypeId) { staticSkill = &candidate; break; }
                    }
                    if (!staticSkill) {
                        for (const auto& [candidateTypeId, definition] : catalog) {
                            (void)candidateTypeId;
                            for (const auto& candidate : definition.skills)
                                if (candidate.typeId == skillTypeId) { staticSkill = &candidate; break; }
                            if (staticSkill) break;
                        }
                    }
                    if (!staticSkill) continue;
                    if (!firstSkill) output << ',';
                    firstSkill = false;
                     output << "{\"typeId\":" << skillTypeId << ",\"slot\":" << currentSkillSlot
                            << ",\"name\":\"" << jsonEscape(staticSkill->name)
                            << "\",\"target\":" << staticSkill->target
                            << ",\"cooldown\":" << cooldown << ",\"maxCooldown\":" << maxCooldown
                            << ",\"requiresTarget\":" << (staticSkill->requiresTarget ? "true" : "false")
                            << ",\"disabled\":" << (fieldValue<bool>(skill, "<Disabled>k__BackingField") ? "true" : "false") << '}';
                }
            }
            output << "],\"effects\":[";
            bool firstEffect = true;
            if (auto* applied = fieldValue<Il2CppObject*>(hero, "AppliedEffectsByHeroes")) {
                for (auto* effectList : dictionaryValues(applied)) {
                    if (!effectList) continue;
                    for (auto* effect : referenceList(effectList)) {
                        if (!effect) continue;
                        const auto effectTypeId = fieldValue<int32_t>(effect, "EffectTypeId");
                        const auto turns = fieldValue<int32_t>(effect, "TurnLeft");
                        if (effectTypeId <= 0 || turns < -1 || turns > 1000) throw std::runtime_error("A battle effect value is outside the supported range.");
                        if (!firstEffect) output << ',';
                        firstEffect = false;
                        output << "{\"typeId\":" << effectTypeId << ",\"turns\":" << turns << '}';
                    }
                }
            }
            output << "]}";
        }
    }
    auto* hud = visibleBattleHud();
    const auto modeChangeAvailable = hud != nullptr;
    const auto skillSelectionAvailable = hud && invokeValue<bool>(objectField(hud, "SelectSkillEnabled"), "get_Value");
    const auto hudSkills = hud ? referenceList(objectField(objectField(hud, "_activeSkills"), "_list")) : std::vector<Il2CppObject*>{};
    const auto hudSkillCount = hudSkills.size();
    if (hudSkillCount > 12) throw std::runtime_error("The battle HUD exposes an implausible skill count.");
    output << "],\"hudVisible\":" << (hud ? "true" : "false")
           << ",\"modeChangeAvailable\":" << (modeChangeAvailable ? "true" : "false")
           << ",\"skillSelectionAvailable\":" << (skillSelectionAvailable ? "true" : "false")
           << ",\"hudSkillCount\":" << hudSkillCount << ",\"hudSkills\":[";
    for (size_t index = 0; index < hudSkills.size(); ++index) {
        auto* skillContext = hudSkills[index];
        auto* data = skillContext ? fieldValue<Il2CppObject*>(skillContext, "<Data>k__BackingField") : nullptr;
        if (!data) throw std::runtime_error("The battle HUD contains an invalid skill context.");
        const auto typeId = fieldValue<int32_t>(data, "TypeId");
        const auto cooldown = invokeValue<int32_t>(skillContext, "get_Cooldown");
        const auto passive = invokeValue<bool>(skillContext, "get_IsPassive");
        if (typeId <= 0 || cooldown < 0 || cooldown > 100)
            throw std::runtime_error("A battle HUD skill value is outside the supported range.");
        if (index) output << ',';
        output << "{\"index\":" << index << ",\"typeId\":" << typeId << ",\"cooldown\":" << cooldown
               << ",\"passive\":" << (passive ? "true" : "false") << '}';
    }
    output << "]}";
    return output.str();
}

template<typename T> void appendNullable(std::ostringstream& output, const std::optional<T>& value) {
    if (value) output << *value;
    else output << "null";
}

const char* draftPhaseName(int32_t phase) {
    switch (phase) {
        case 1: return "initialize";
        case 2: return "heroPick";
        case 3: return "heroBan";
        case 4: return "leaderSelection";
        case 5: return "startBattle";
        case 10: return "opponentCanceled";
        default: throw std::runtime_error("A Live Arena draft phase is outside the supported range.");
    }
}

const char* battlePhaseName(int32_t phase) {
    switch (phase) {
        case 1: return "connection";
        case 2: return "battleTurn";
        case 3: return "finishBattle";
        case 4: return "canceled";
        default: throw std::runtime_error("A Live Arena battle phase is outside the supported range.");
    }
}

struct LiveArenaDraftRules {
    std::optional<int32_t> leagueId;
    std::optional<bool> allowDuplicatePicks;
    std::optional<int32_t> secondsRemaining;
    std::optional<int32_t> turnSeconds;
};

struct LiveArenaUiState {
    bool menuVisible{};
    bool draftVisible{};
    bool queueAvailable{};
    bool finishVisible{};
    bool refillVisible{};
    bool refillCanConfirm{};
    bool rewardOverlayVisible{};
    bool rewardBatchReady{};
    bool dailyBattleRefillReady{};
    int32_t rewardClaimableCount{};
    int32_t refillGemPrice{};
};

LiveArenaDraftRules liveArenaDraftRules();
LiveArenaUiState liveArenaUiState();

std::string liveArenaSnapshot(Il2CppObject* app, const std::map<int, Definition>& catalog, int64_t userId, bool& battleActive) {
    auto* wrapper = objectField(app, "_userWrapper");
    auto* liveArena = objectField(wrapper, "LiveArena");
    auto* draft = objectField(liveArena, "<Draft>k__BackingField");
    auto* battle = objectField(liveArena, "<Battle>k__BackingField");

    const auto position = invokeNullable<int32_t>(liveArena, "get_Position");
    const auto draftRevision = invokeNullable<int32_t>(draft, "get_LastRevision");
    const auto draftPhase = invokeNullable<int32_t>(draft, "get_Phase");
    const auto firstTurnUserId = invokeNullable<int64_t>(draft, "get_FirstTurnUserId");
    const auto turnUserId = invokeNullable<int64_t>(draft, "get_UserIdTurn");
    const auto bestEnemyBlockedSlot = invokeNullable<int32_t>(draft, "get_BestEnemyBlockedSlotIndex");
    const auto playerBlockedSlot = invokeNullable<int32_t>(draft, "get_PlayerBlockedSlotIndex");
    const auto enemyBlockedSlot = invokeNullable<int32_t>(draft, "get_EnemyBlockedSlotIndex");
    const auto playerLeaderSlot = invokeNullable<int32_t>(draft, "get_PlayerLeaderSlotIndex");
    const auto enemyLeaderSlot = invokeNullable<int32_t>(draft, "get_EnemyLeaderSlotIndex");
    auto* playerSelected = objectField(draft, "<PlayerSelectedHeroes>k__BackingField");
    auto* enemySelected = objectField(draft, "<EnemySelectedHeroes>k__BackingField");
    const auto playerHeroes = referenceList(playerSelected);
    const auto enemyHeroes = referenceList(enemySelected);
    const auto draftRules = liveArenaDraftRules();
    const auto ui = liveArenaUiState();

    const auto battleRevision = invokeNullable<int32_t>(battle, "get_LastRevision");
    const auto turnRevision = invokeNullable<int32_t>(battle, "get_TurnRevision");
    const auto battlePhase = invokeNullable<int32_t>(battle, "get_Phase");
    const auto activeUserId = invokeNullable<int64_t>(battle, "get_ActiveUserId");
    const auto commandCount = invokeValue<int32_t>(battle, "get_CommandCount");
    battleActive = fieldValue<bool>(battle, "<Active>k__BackingField");
    const auto friendly = fieldValue<bool>(battle, "<IsFriendlyBattle>k__BackingField");
    const auto finished = invokeValue<bool>(battle, "get_IsFinished");

    const auto validRevision = [](const auto& value) { return !value || *value >= 0; };
    const auto validSlot = [](const auto& value) { return !value || (*value >= 0 && *value < 5); };
    if ((position && *position <= 0) || !validRevision(draftRevision) || !validRevision(battleRevision) || !validRevision(turnRevision)
        || !validSlot(bestEnemyBlockedSlot) || !validSlot(playerBlockedSlot) || !validSlot(enemyBlockedSlot)
        || !validSlot(playerLeaderSlot) || !validSlot(enemyLeaderSlot) || playerHeroes.size() > 5 || enemyHeroes.size() > 5
        || commandCount < 0 || commandCount > 100000)
        throw std::runtime_error("A Live Arena value is outside the supported range.");

    std::ostringstream output;
    output << "{\"protocol\":1,\"type\":\"liveArena\",\"matchmaking\":"
           << (fieldValue<bool>(liveArena, "<IsMatchMakingInProcess>k__BackingField") ? "true" : "false") << ",\"position\":";
    appendNullable(output, position);
    output << ",\"ui\":{\"menuVisible\":" << (ui.menuVisible ? "true" : "false")
           << ",\"draftVisible\":" << (ui.draftVisible ? "true" : "false")
           << ",\"queueAvailable\":" << (ui.queueAvailable ? "true" : "false")
           << ",\"finishVisible\":" << (ui.finishVisible ? "true" : "false")
           << ",\"refillVisible\":" << (ui.refillVisible ? "true" : "false")
           << ",\"refillCanConfirm\":" << (ui.refillCanConfirm ? "true" : "false")
           << ",\"rewardOverlayVisible\":" << (ui.rewardOverlayVisible ? "true" : "false")
           << ",\"rewardBatchReady\":" << (ui.rewardBatchReady ? "true" : "false")
           << ",\"dailyBattleRefillReady\":" << (ui.dailyBattleRefillReady ? "true" : "false")
           << ",\"rewardClaimableCount\":" << ui.rewardClaimableCount
           << ",\"refillGemPrice\":" << ui.refillGemPrice
           << "},\"draft\":{\"revision\":";
    appendNullable(output, draftRevision);
    output << ",\"phase\":" << (draftPhase ? "\"" + std::string(draftPhaseName(*draftPhase)) + "\"" : "null")
           << ",\"firstTurn\":" << (firstTurnUserId ? (*firstTurnUserId == userId ? "\"player\"" : "\"opponent\"") : "null")
           << ",\"turn\":" << (turnUserId ? (*turnUserId == userId ? "\"player\"" : "\"opponent\"") : "null")
           << ",\"leagueId\":";
    appendNullable(output, draftRules.leagueId);
    output << ",\"allowDuplicatePicks\":";
    if (draftRules.allowDuplicatePicks) output << (*draftRules.allowDuplicatePicks ? "true" : "false");
    else output << "null";
    output << ",\"secondsRemaining\":";
    appendNullable(output, draftRules.secondsRemaining);
    output << ",\"turnSeconds\":";
    appendNullable(output, draftRules.turnSeconds);
    output << ",\"playerHeroes\":[";
    for (size_t index = 0; index < playerHeroes.size(); ++index) {
        auto* hero = playerHeroes[index];
        requireObject(hero, "A selected player champion is invalid.");
        const auto id = fieldValue<int32_t>(hero, "Id");
        const auto typeId = fieldValue<int32_t>(hero, "TypeId");
        if (id <= 0 || typeId <= 0) throw std::runtime_error("A selected player champion value is invalid.");
        const auto found = catalog.find(typeId);
        if (found == catalog.end()) throw std::runtime_error("A selected player champion type is unknown.");
        const auto name = found->second.name.empty() ? "Hero " + std::to_string(typeId) : found->second.name;
        if (index) output << ',';
        output << "{\"slot\":" << index << ",\"id\":" << id << ",\"typeId\":" << typeId << ",\"baseId\":" << found->second.baseId << ",\"name\":\"" << jsonEscape(name) << "\"}";
    }
    output << "],\"enemyHeroes\":[";
    for (size_t index = 0; index < enemyHeroes.size(); ++index) {
        auto* entry = enemyHeroes[index];
        requireObject(entry, "A selected opponent champion is invalid.");
        auto* hero = objectField(entry, "Hero");
        const auto typeId = fieldValue<int32_t>(hero, "TypeId");
        if (typeId <= 0) throw std::runtime_error("A selected opponent champion value is invalid.");
        const auto found = catalog.find(typeId);
        if (found == catalog.end()) throw std::runtime_error("A selected opponent champion type is unknown.");
        const auto name = found->second.name.empty() ? "Hero " + std::to_string(typeId) : found->second.name;
        if (index) output << ',';
        output << "{\"slot\":" << index << ",\"typeId\":" << typeId << ",\"baseId\":" << found->second.baseId << ",\"name\":\"" << jsonEscape(name) << "\"}";
    }
    output << "],\"bestEnemyBlockedSlot\":";
    appendNullable(output, bestEnemyBlockedSlot);
    output << ",\"playerBlockedSlot\":";
    appendNullable(output, playerBlockedSlot);
    output << ",\"enemyBlockedSlot\":";
    appendNullable(output, enemyBlockedSlot);
    output << ",\"playerLeaderSlot\":";
    appendNullable(output, playerLeaderSlot);
    output << ",\"enemyLeaderSlot\":";
    appendNullable(output, enemyLeaderSlot);
    output << ",\"battleSetupReady\":" << (fieldValue<Il2CppObject*>(draft, "<BattleSetup>k__BackingField") ? "true" : "false")
           << "},\"transport\":{\"active\":" << (battleActive ? "true" : "false") << ",\"friendly\":" << (friendly ? "true" : "false")
           << ",\"finished\":" << (finished ? "true" : "false") << ",\"revision\":";
    appendNullable(output, battleRevision);
    output << ",\"turnRevision\":";
    appendNullable(output, turnRevision);
    output << ",\"phase\":" << (battlePhase ? "\"" + std::string(battlePhaseName(*battlePhase)) + "\"" : "null")
           << ",\"turn\":" << (activeUserId ? (*activeUserId == userId ? "\"player\"" : "\"opponent\"") : "null")
           << ",\"queuedCommands\":" << commandCount << "}}";
    return output.str();
}

bool isTypeOrSubclass(Il2CppClass* type, Il2CppClass* expected) {
    for (auto* current = type; current; current = api.classParent(current))
        if (current == expected) return true;
    return false;
}

Il2CppObject* directStaticInstance(Il2CppClass* expectedType) {
    if (!expectedType) return nullptr;
    for (auto* current = expectedType; current; current = api.classParent(current)) {
        void* iterator = nullptr;
        while (auto* field = api.classFields(current, &iterator)) {
            if ((api.fieldFlags(field) & 0x10u) == 0) continue;
            const char* rawName = api.fieldName(field);
            if (!rawName) continue;
            const std::string name(rawName);
            if (name != "Instance" && name != "_instance" && name != "<Instance>k__BackingField") continue;
            Il2CppObject* value = nullptr;
            api.fieldStaticValue(field, &value);
            if (!value || !readable(value, 16)) continue;
            auto* valueType = api.objectClass(value);
            if (valueType && isTypeOrSubclass(valueType, expectedType)) return value;
        }
    }
    return nullptr;
}

bool catalogRequiresExplicitTarget(const std::map<int, Definition>& catalog, int32_t skillTypeId) {
    if (skillTypeId <= 0) throw std::runtime_error("The configured battle skill identifier is invalid.");
    for (const auto& [typeId, definition] : catalog) {
        (void)typeId;
        for (const auto& skill : definition.skills) {
            if (skill.typeId == skillTypeId) return skill.requiresTarget;
        }
    }
    throw std::runtime_error("The configured battle skill is not present in RAID's validated static catalog.");
}

std::string il2CppTypeName(const Il2CppType* type);

bool isReferenceType(const Il2CppType* type) {
    if (!type) return false;
    auto* typeClass = api.classFromType(type);
    return typeClass && !api.classIsValueType(typeClass);
}

Il2CppObject* visibleContext(Il2CppClass* expectedType, bool passive = false) {
    static auto* appViewModelType = api.findClass("Client.ViewModel", "AppViewModel");
    auto* appViewModel = passive ? directStaticInstance(appViewModelType) : nullptr;
    if (!passive) {
        Il2CppObject* exception = nullptr;
        appViewModel = api.runtimeInvoke(api.method(appViewModelType, "get_Instance"), nullptr, nullptr, &exception);
        if (exception) return nullptr;
    }
    if (!appViewModel) return nullptr;
    auto* overlays = fieldValue<Il2CppObject*>(appViewModel, "_overlayManager");
    if (!overlays) return nullptr;
    auto* viewMaster = fieldValue<Il2CppObject*>(overlays, "_viewMaster");
    if (!viewMaster) return nullptr;
    auto* views = fieldValue<Il2CppObject*>(viewMaster, "_views");
    if (!views) return nullptr;
    for (auto* meta : referenceList(views)) {
        if (!meta || fieldValue<int32_t>(meta, "State") != 1 || fieldValue<int32_t>(meta, "Visibility") != 1) continue;
        auto* view = fieldValue<Il2CppObject*>(meta, "View");
        if (!view) continue;
        auto* context = fieldValue<Il2CppObject*>(view, "<Context>k__BackingField");
        if (context && isTypeOrSubclass(api.objectClass(context), expectedType)) return context;
    }
    return nullptr;
}

std::string il2CppTypeName(const Il2CppType* type) {
    if (!type) return "unknown";
    char* allocated = api.typeName(type);
    if (!allocated) return "unknown";
    std::string result(allocated);
    api.freeMemory(allocated);
    return result.size() <= 256 ? result : "invalid";
}

std::string il2CppMethodSignature(const MethodInfo* method) {
    if (!method) return "invalid";
    const char* rawName = api.methodName(method);
    if (!rawName || !*rawName || std::char_traits<char>::length(rawName) > 256) return "invalid";
    const auto parameterCount = api.methodParamCount(method);
    if (parameterCount > 32) return "invalid";
    std::ostringstream output;
    output << rawName << '(';
    for (uint32_t index = 0; index < parameterCount; ++index) {
        if (index) output << ',';
        output << il2CppTypeName(api.methodParam(method, index));
    }
    output << "):" << il2CppTypeName(api.methodReturnType(method));
    const auto result = output.str();
    return result.size() <= 1024 ? result : "invalid";
}

std::string il2CppClassName(Il2CppClass* type) {
    if (!type) return "unknown";
    const char* name = api.className(type);
    const char* nameSpace = api.classNamespace(type);
    if (!name || !nameSpace) return "unknown";
    std::string result;
    if (*nameSpace) result = std::string(nameSpace) + '.';
    result += name;
    return result.size() <= 256 ? result : "invalid";
}

void logPriceRuntimeInventory(Il2CppObject* resources) {
    static std::unordered_set<Il2CppClass*> logged;
    auto* type = api.objectClass(resources);
    if (!type || logged.size() >= 8 || !logged.insert(type).second) return;
    std::ostringstream output;
    output << "Refill price runtime inventory: type=" << il2CppClassName(type) << "; fields=[";
    size_t fieldCount = 0;
    for (auto* current = type; current && fieldCount < 128; current = api.classParent(current)) {
        void* iterator = nullptr;
        while (auto* field = api.classFields(current, &iterator)) {
            if (fieldCount++) output << ',';
            output << api.fieldName(field) << ':' << il2CppTypeName(api.fieldType(field));
            if (fieldCount >= 128) break;
        }
    }
    output << "]; methods=[";
    size_t methodCount = 0;
    for (auto* current = type; current && methodCount < 128; current = api.classParent(current)) {
        void* iterator = nullptr;
        while (const auto* method = api.classMethods(current, &iterator)) {
            if (methodCount++) output << ',';
            output << il2CppMethodSignature(method);
            if (methodCount >= 128) break;
        }
    }
    output << ']';
    nativeLog(output.str().substr(0, 32768));
}

int32_t boundedGemPrice(Il2CppObject* resources, const char* error) {
    if (il2CppClassName(api.objectClass(resources)) == "Client.ViewModel.Contextes.ResourcesContext") {
        Il2CppObject* exception = nullptr;
        resources = api.runtimeInvoke(api.method(api.objectClass(resources), "get_Value", 0), resources, nullptr, &exception);
        if (exception || !resources || il2CppClassName(api.objectClass(resources)) != "SharedModel.Meta.Account.Resources")
            throw std::runtime_error("The refill price ResourcesContext did not expose its exact Resources value.");
    }
    const MethodInfo* single = nullptr;
    const MethodInfo* gemsGetter = nullptr;
    for (auto* current = api.objectClass(resources); current; current = api.classParent(current)) {
        if (!single) single = api.classMethod(current, "GetSingle", 0);
        if (!gemsGetter) gemsGetter = api.classMethod(current, "get_Gems", 0);
    }
    if (!single || !gemsGetter) {
        logPriceRuntimeInventory(resources);
        throw std::runtime_error("The refill Resources value does not expose the pinned single-resource Gems accessors.");
    }
    constexpr int32_t GemResourceType = 4;
    const auto resource = invokeValue<int32_t>(resources, "GetSingle");
    const auto gems = invokeValue<double>(resources, "get_Gems");
    if (resource != GemResourceType || !std::isfinite(gems) || gems < 1.0 || gems > 10000.0 || std::floor(gems) != gems)
        throw std::runtime_error(error);
    return static_cast<int32_t>(gems);
}

void appendBattleMethodInventory(std::ostringstream& output, Il2CppClass* type, const char* label) {
    output << "{\"label\":\"" << jsonEscape(label) << "\",\"class\":\""
           << jsonEscape(il2CppClassName(type)) << "\",\"methods\":[";
    std::unordered_set<std::string> signatures;
    size_t methodCount = 0;
    for (auto* current = type; current && signatures.size() < 256; current = api.classParent(current)) {
        void* iterator = nullptr;
        while (const auto* method = api.classMethods(current, &iterator)) {
            if (++methodCount > 2048) throw std::runtime_error("The battle method inventory exceeded its safety limit.");
            const auto signature = il2CppMethodSignature(method);
            if (signature != "invalid") signatures.emplace(signature);
            if (signatures.size() >= 256) break;
        }
    }
    size_t index = 0;
    for (const auto& signature : signatures) {
        if (index++) output << ',';
        output << '\"' << jsonEscape(signature) << '\"';
    }
    output << "]}";
}

std::string battleUiMethodInventory() {
    auto* hud = visibleBattleHud();
    if (!hud) return "{\"available\":false}";
    std::ostringstream output;
    output << "{\"available\":true,\"contexts\":[";
    size_t contextCount = 0;
    std::unordered_set<Il2CppClass*> classes;
    const auto add = [&](const char* label, Il2CppObject* object) {
        if (!object) return;
        auto* type = api.objectClass(object);
        if (!type || !classes.emplace(type).second) return;
        if (contextCount++) output << ',';
        appendBattleMethodInventory(output, type, label);
    };
    add("BattleHUDContext", hud);
    if (auto* activeSkills = objectField(hud, "_activeSkills")) {
        if (auto* list = objectField(activeSkills, "_list")) {
            const auto contexts = referenceList(list);
            for (auto* skillContext : contexts) {
                add("SkillContext", skillContext);
                if (skillContext) add("SkillData", fieldValue<Il2CppObject*>(skillContext, "<Data>k__BackingField"));
                if (contextCount >= 4) break;
            }
        }
    }
    output << "]}";
    const auto result = output.str();
    if (result.size() > 512 * 1024) throw std::runtime_error("The battle method inventory exceeds the size limit.");
    return result;
}

void appendTraceFieldValue(std::ostringstream& output, Il2CppObject* object, FieldInfo* field, const std::string& typeName) {
    if (typeName == "System.Boolean") {
        bool value{};
        api.fieldValue(object, field, &value);
        output << (value ? "true" : "false");
    } else if (typeName == "System.Int32" || typeName == "System.UInt32") {
        int32_t value{};
        api.fieldValue(object, field, &value);
        output << value;
    } else if (typeName == "System.Int64" || typeName == "System.UInt64") {
        int64_t value{};
        api.fieldValue(object, field, &value);
        output << value;
    } else if (typeName == "System.Single") {
        float value{};
        api.fieldValue(object, field, &value);
        if (std::isfinite(value)) output << value;
        else output << "null";
    } else if (typeName == "System.Double") {
        double value{};
        api.fieldValue(object, field, &value);
        if (std::isfinite(value)) output << value;
        else output << "null";
    } else {
        output << "null";
    }
}

std::string visibleContextTraceSnapshot(const std::string& traceType) {
    if (traceType != "rewardTrace")
        throw std::runtime_error("The visible context diagnostic type is invalid.");
    static auto* appViewModelType = api.findClass("Client.ViewModel", "AppViewModel");
    Il2CppObject* exception = nullptr;
    auto* appViewModel = api.runtimeInvoke(api.method(appViewModelType, "get_Instance"), nullptr, nullptr, &exception);
    if (exception || !appViewModel) throw std::runtime_error("The visible RAID view inventory is unavailable.");
    auto* overlays = fieldValue<Il2CppObject*>(appViewModel, "_overlayManager");
    auto* viewMaster = overlays ? fieldValue<Il2CppObject*>(overlays, "_viewMaster") : nullptr;
    auto* views = viewMaster ? fieldValue<Il2CppObject*>(viewMaster, "_views") : nullptr;
    if (!views) throw std::runtime_error("The visible RAID view list is unavailable.");

    std::ostringstream output;
    output << "{\"protocol\":1,\"type\":\"" << traceType << "\",\"contexts\":[";
    size_t contextCount = 0;
    for (auto* meta : referenceList(views)) {
        if (!meta || fieldValue<int32_t>(meta, "State") != 1 || fieldValue<int32_t>(meta, "Visibility") != 1) continue;
        auto* view = fieldValue<Il2CppObject*>(meta, "View");
        auto* context = view ? fieldValue<Il2CppObject*>(view, "<Context>k__BackingField") : nullptr;
        if (!context) continue;
        if (contextCount >= 64) throw std::runtime_error("The visible RAID context count exceeds the diagnostic limit.");
        if (contextCount++) output << ',';
        auto* type = api.objectClass(context);
        output << "{\"contextType\":\"" << jsonEscape(il2CppClassName(type)) << "\",\"fields\":[";
        size_t fieldCount = 0;
        for (auto* current = type; current && fieldCount < 128; current = api.classParent(current)) {
            void* iterator = nullptr;
            while (auto* field = api.classFields(current, &iterator)) {
                if ((api.fieldFlags(field) & 0x10u) != 0) continue;
                const char* rawName = api.fieldName(field);
                if (!rawName) continue;
                const std::string name(rawName);
                const auto typeName = il2CppTypeName(api.fieldType(field));
                if (name.size() > 256 || typeName == "invalid") continue;
                if (fieldCount++) output << ',';
                output << "{\"name\":\"" << jsonEscape(name) << "\",\"fieldType\":\"" << jsonEscape(typeName) << "\",\"value\":";
                appendTraceFieldValue(output, context, field, typeName);
                output << '}';
                if (fieldCount >= 128) break;
            }
        }
        output << "],\"methods\":[";
        std::unordered_set<std::string> methodNames;
        for (auto* current = type; current && methodNames.size() < 128; current = api.classParent(current)) {
            void* iterator = nullptr;
            while (const auto* method = api.classMethods(current, &iterator)) {
                const char* rawName = api.methodName(method);
                if (rawName && *rawName && std::char_traits<char>::length(rawName) <= 256) methodNames.emplace(rawName);
                if (methodNames.size() >= 128) break;
            }
        }
        size_t methodIndex = 0;
        for (const auto& name : methodNames) {
            if (methodIndex++) output << ',';
            output << '\"' << jsonEscape(name) << '\"';
        }
        output << "]}";
    }
    output << "]}";
    if (output.tellp() > 512 * 1024) throw std::runtime_error("The visible context diagnostic snapshot exceeds the size limit.");
    return output.str();
}

Il2CppObject* visibleLiveArenaDialog() {
    static auto* dialogType = api.findClass("Client.ViewModel.Contextes.LiveArenaDraftDialog", "LiveArenaDraftDialogContext");
    return visibleContext(dialogType);
}

Il2CppObject* visibleLiveArenaMenu() {
    static auto* menuType = api.findClass("Client.ViewModel.Contextes.LiveArenaDialog", "LiveArenaDialogContext");
    return visibleContext(menuType);
}

Il2CppObject* visibleLiveArenaRefill() {
    static auto* refillType = api.findClass("Client.ViewModel.Contextes", "ResourceRefillOverlayContext");
    auto* refill = visibleContext(refillType);
    return refill && fieldValue<int32_t>(refill, "<CurrentState>k__BackingField") == 7 ? refill : nullptr;
}

Il2CppObject* visibleLiveArenaFinish() {
    static auto* dialogType = api.findClass("Client.ViewModel.Contextes.BattleFinishDialog", "BattleFinishLiveArenaDialogContext");
    if (auto* dialog = visibleContext(dialogType)) return dialog;
    static auto* overlayType = api.findClass("Client.ViewModel.Contextes.LiveArenaBattleFinishOverlay", "LiveArenaBattleFinishOverlayContext");
    return visibleContext(overlayType);
}

Il2CppObject* visibleLiveArenaRewardOverlay(bool& genericGacha) {
    static auto* gachaType = api.findClass("Client.ViewModel.Contextes.GachaChest", "GachaChestOverlayContext");
    if (auto* overlay = visibleContext(gachaType)) {
        genericGacha = true;
        return overlay;
    }
    static auto* repeatableType = api.findClass("Client.ViewModel.Contextes", "LiveArenaRepeatableChestOverlayContext");
    genericGacha = false;
    return visibleContext(repeatableType);
}

int32_t rewardStateProperty(Il2CppObject* context, const char* field) {
    const auto state = invokeValue<int32_t>(objectField(context, field), "get_Value");
    if (state < 0 || state > 3) throw std::runtime_error("A Live Arena reward state is outside the supported range.");
    return state;
}

std::vector<Il2CppObject*> liveArenaDailyWins(Il2CppObject* battleTab) {
    auto* contexts = objectField(battleTab, "_dailyWinsRewards");
    auto* list = objectField(contexts, "_list");
    auto result = referenceList(list);
    if (result.size() != 3) throw std::runtime_error("RAID did not expose exactly three Live Arena daily-win rewards.");
    return result;
}

void applyLiveArenaRewardState(LiveArenaUiState& result, Il2CppObject* menu) {
    bool genericGacha = false;
    result.rewardOverlayVisible = visibleLiveArenaRewardOverlay(genericGacha) != nullptr;
    if (!menu || invokeValue<int32_t>(menu, "get_Current") != 0) return;
    auto* battleTab = objectField(menu, "_battleTab");
    auto* dailyQuest = objectField(battleTab, "_dailyQuest");
    const auto dailyQuestState = fieldValue<int32_t>(dailyQuest, "_currentRewardState");
    if (dailyQuestState < 0 || dailyQuestState > 3) throw std::runtime_error("The Live Arena daily quest reward state is invalid.");
    const auto dailyWins = liveArenaDailyWins(battleTab);
    std::vector<int32_t> dailyWinStates;
    dailyWinStates.reserve(dailyWins.size());
    for (auto* stage : dailyWins) dailyWinStates.push_back(rewardStateProperty(stage, "_state"));
    result.rewardClaimableCount = (dailyQuestState == 2 ? 1 : 0)
        + static_cast<int32_t>(std::count(dailyWinStates.begin(), dailyWinStates.end(), 2));
    result.dailyBattleRefillReady = dailyQuestState == 2;
    result.rewardBatchReady = dailyQuestState == 2
        && std::all_of(dailyWinStates.begin(), dailyWinStates.end(), [](int32_t state) { return state == 2; });
}

bool liveArenaQueueAvailable(Il2CppObject* menu) {
    if (!menu || invokeValue<int32_t>(menu, "get_Current") != 0) return false;
    auto* battleTab = objectField(menu, "_battleTab");
    return fieldValue<bool>(battleTab, "_sceneLoaded")
        && invokeValue<int32_t>(objectField(battleTab, "_battleButtonState"), "get_Value") == 1
        && invokeValue<bool>(objectField(battleTab, "_battleButtonEnabled"), "get_Value")
        && !invokeValue<bool>(objectField(battleTab, "_uiHidden"), "get_Value");
}

LiveArenaUiState liveArenaUiState() {
    LiveArenaUiState result;
    auto* menu = visibleLiveArenaMenu();
    auto* draft = visibleLiveArenaDialog();
    auto* refill = visibleLiveArenaRefill();
    result.menuVisible = menu != nullptr;
    result.draftVisible = draft && fieldValue<Il2CppObject*>(draft, "_phase") != nullptr;
    result.queueAvailable = liveArenaQueueAvailable(menu);
    result.finishVisible = visibleLiveArenaFinish() != nullptr;
    result.refillVisible = refill != nullptr;
    result.refillCanConfirm = refill
        && invokeValue<bool>(objectField(refill, "_canPurchase"), "get_Value")
        && invokeValue<int32_t>(objectField(refill, "_count"), "get_Value") > 0;
    if (refill && result.refillCanConfirm) {
        auto* ownedResourceProperty = objectField(refill, "_ownedResource");
        const auto ownedResource = invokeValue<bool>(ownedResourceProperty, "get_Value");
        if (!ownedResource) {
            auto* price = objectField(refill, "_price");
            try {
                result.refillGemPrice = boundedGemPrice(price, "The visible Live Arena refill is not a bounded Gems purchase.");
            } catch (const std::exception& exception) {
                result.refillCanConfirm = false;
                nativeLog(std::string("Live Arena refill price observation deferred: ") + exception.what());
            }
        }
    }
    applyLiveArenaRewardState(result, menu);
    return result;
}

LiveArenaDraftRules liveArenaDraftRules() {
    auto* dialog = visibleLiveArenaDialog();
    if (!dialog) return {};
    const auto leagueId = fieldValue<int32_t>(dialog, "_leagueId");
    auto* phase = fieldValue<Il2CppObject*>(dialog, "_phase");
    auto* state = phase ? fieldValue<Il2CppObject*>(phase, "_iterativeState") : nullptr;
    auto* timer = phase ? fieldValue<Il2CppObject*>(phase, "_draftTimer") : nullptr;
    static auto* heroesPickStateType = api.findClass("Client.ViewModel.Contextes.LiveArenaDraftDialog.State", "HeroesPickState");
    std::optional<bool> allowDuplicatePicks;
    if (leagueId >= 1 && leagueId <= 14) allowDuplicatePicks = true;
    else if (leagueId >= 21) allowDuplicatePicks = false;
    if (state && isTypeOrSubclass(api.objectClass(state), heroesPickStateType))
        allowDuplicatePicks = fieldValue<bool>(state, "_allowToDuplicateHeroes");
    const auto timerValue = [&](const char* field) -> std::optional<int32_t> {
        auto* property = timer ? fieldValue<Il2CppObject*>(timer, field) : nullptr;
        if (!property) return std::nullopt;
        const auto value = invokeValue<double>(property, "get_Value");
        if (!std::isfinite(value) || value < 0 || value > 600) return std::nullopt;
        return static_cast<int32_t>(std::ceil(value));
    };
    return { leagueId, allowDuplicatePicks, timerValue("_leftTime"), timerValue("_turnTime") };
}

Il2CppObject* liveArenaSelectionContext() {
    auto* dialog = visibleLiveArenaDialog();
    return dialog ? fieldValue<Il2CppObject*>(dialog, "_phase") : nullptr;
}

void requireLiveArenaTurn(Il2CppObject* context, int32_t phase) {
    if (!context) throw std::runtime_error("The Live Arena draft screen is not visible.");
    if (fieldValue<int32_t>(context, "_phaseType") != phase) throw std::runtime_error("The Live Arena draft phase changed before the action could be applied.");
    if (fieldValue<bool>(context, "_executingCmd")) throw std::runtime_error("RAID is already submitting a Live Arena command.");
    auto* isPlayerTurn = objectField(context, "_isPlayerTurn");
    if (!invokeValue<bool>(isPlayerTurn, "get_Value")) throw std::runtime_error("It is no longer the player's Live Arena turn.");
}

void confirmLiveArena(Il2CppObject* context) {
    auto* state = objectField(context, "_iterativeState");
    if (!invokeValue<bool>(state, "CanConfirm")) throw std::runtime_error("RAID did not accept the requested Live Arena selection.");
    Il2CppObject* exception = nullptr;
    api.runtimeInvoke(api.method(api.objectClass(context), "Confirm"), context, nullptr, &exception);
    if (exception) throw std::runtime_error("RAID rejected the Live Arena confirmation.");
}

int32_t liveArenaHeroIdFromSlot(Il2CppObject* squad, int32_t slot, bool requireHero) {
    void* arguments[] = {&slot};
    Il2CppObject* exception = nullptr;
    auto* boxed = api.runtimeInvoke(api.method(api.objectClass(squad), "HeroIdFromSlot", 1), squad, arguments, &exception);
    if (exception || !boxed) throw std::runtime_error("The requested Live Arena slot is unavailable.");
    auto* value = static_cast<int32_t*>(api.objectUnbox(boxed));
    if (!readable(value, sizeof(int32_t)) || (requireHero && *value <= 0)) throw std::runtime_error("The requested Live Arena slot has no champion.");
    return *value;
}

void pickLiveArena(Il2CppObject* context, const std::vector<int32_t>& heroes) {
    requireLiveArenaTurn(context, 2);
    const auto* method = api.method(api.objectClass(context), "HeroPicked", 2);
    for (auto heroId : heroes) {
        // RAID passes the avatar's state before the click: false requests selection, true requests removal.
        bool wasSelected = false;
        void* arguments[] = {&heroId, &wasSelected};
        Il2CppObject* exception = nullptr;
        api.runtimeInvoke(method, context, arguments, &exception);
        if (exception) throw std::runtime_error("RAID rejected a Live Arena champion pick.");
    }
    auto* squad = objectField(context, "_mySquad");
    std::vector<int32_t> selected;
    for (int32_t slot = 0; slot < 5; ++slot) {
        const auto id = liveArenaHeroIdFromSlot(squad, slot, false);
        if (id > 0) selected.push_back(id);
    }
    if (std::any_of(heroes.begin(), heroes.end(), [&](int32_t id) { return std::find(selected.begin(), selected.end(), id) == selected.end(); }))
        throw std::runtime_error("RAID did not place every requested champion into the Live Arena squad.");
    nativeLog("Live Arena champion selection applied; confirmation will run on the next main-thread pass.");
}

void selectLiveArenaSlot(Il2CppObject* context, int32_t phase, int32_t slot, const char* squadField) {
    requireLiveArenaTurn(context, phase);
    auto* squad = objectField(context, squadField);
    auto heroId = liveArenaHeroIdFromSlot(squad, slot, true);
    void* arguments[] = {squad, &heroId};
    Il2CppObject* exception = nullptr;
    api.runtimeInvoke(api.method(api.objectClass(context), "TrySelectSlot", 2), context, arguments, &exception);
    if (exception) throw std::runtime_error("RAID rejected the requested Live Arena slot.");
    const auto selected = invokeNullable<int32_t>(squad, "get_SelectedSlotIndex");
    if (!selected || *selected != slot) throw std::runtime_error("RAID did not select the requested Live Arena slot.");
    confirmLiveArena(context);
}

void queueLiveArena() {
    auto* menu = visibleLiveArenaMenu();
    if (!liveArenaQueueAvailable(menu)) throw std::runtime_error("The Live Arena battle menu is not ready to start matchmaking.");
    auto* battleTab = objectField(menu, "_battleTab");
    Il2CppObject* exception = nullptr;
    api.runtimeInvoke(api.method(api.objectClass(battleTab), "MatchMakingClick"), battleTab, nullptr, &exception);
    if (exception) throw std::runtime_error("RAID rejected the Live Arena matchmaking request.");
}

void refillLiveArena(int32_t expectedGemPrice) {
    if (expectedGemPrice < 0 || expectedGemPrice > 10000)
        throw std::runtime_error("The expected Live Arena refill Gem price is outside the supported range.");
    auto* refill = visibleLiveArenaRefill();
    if (!refill) throw std::runtime_error("A Live Arena token refill is not visibly requested.");
    auto* ownedResourceProperty = fieldValue<Il2CppObject*>(refill, "_ownedResource");
    auto* ownedItem = fieldValue<Il2CppObject*>(refill, "_ownedItem");
    if (!ownedResourceProperty) throw std::runtime_error("RAID did not expose the Live Arena free-refill state.");
    const auto ownedResource = invokeValue<bool>(ownedResourceProperty, "get_Value");
    if (ownedResource) {
        if (expectedGemPrice != 0) throw std::runtime_error("The visible Live Arena refill became free before confirmation.");
        if (!ownedItem) throw std::runtime_error("Live Arena free-refill state has no owned refill item.");
        auto* method = api.method(api.objectClass(refill), "ApplyItem", 0);
        if (!method) throw std::runtime_error("The Live Arena free-refill action is unavailable.");
        Il2CppObject* exception = nullptr;
        api.runtimeInvoke(method, refill, nullptr, &exception);
        if (exception) throw std::runtime_error("RAID rejected the Live Arena free refill.");
        return;
    }
    if (ownedItem) throw std::runtime_error("Live Arena refill state is inconsistent: no free refill is available but an owned item is present.");
    const auto canPurchase = invokeValue<bool>(objectField(refill, "_canPurchase"), "get_Value");
    const auto count = invokeValue<int32_t>(objectField(refill, "_count"), "get_Value");
    if (!canPurchase || count <= 0 || count > 100) throw std::runtime_error("The visible Live Arena token refill cannot be safely confirmed.");
    auto* price = objectField(refill, "_price");
    const auto gems = boundedGemPrice(price, "The visible Live Arena refill is not a bounded Gems purchase.");
    if (gems != expectedGemPrice)
        throw std::runtime_error("The visible Live Arena refill Gem price changed before confirmation.");
    Il2CppObject* exception = nullptr;
    api.runtimeInvoke(api.method(api.objectClass(refill), "Refill"), refill, nullptr, &exception);
    if (exception) throw std::runtime_error("RAID rejected the Live Arena token refill.");
}

void returnToLiveArena() {
    static auto* dialogType = api.findClass("Client.ViewModel.Contextes.BattleFinishDialog", "BattleFinishLiveArenaDialogContext");
    auto* finish = visibleContext(dialogType);
    if (!finish || !fieldValue<bool>(finish, "_initialized") || !invokeValue<bool>(finish, "get_Enabled"))
        throw std::runtime_error("The traced Live Arena result dialog is not visibly active.");
    const auto* close = api.method(api.objectClass(finish), "Close", 0);
    if (il2CppMethodSignature(close) != "Close():System.Void")
        throw std::runtime_error("The traced Live Arena Return method does not match the pinned build.");
    Il2CppObject* exception = nullptr;
    api.runtimeInvoke(close, finish, nullptr, &exception);
    if (exception) throw std::runtime_error("RAID rejected the return from the Live Arena result screen.");
}

void claimNextLiveArenaReward(int32_t expectedClaimableCount) {
    if (expectedClaimableCount < 1 || expectedClaimableCount > 4) throw std::runtime_error("The Live Arena reward claim count is invalid.");
    auto* menu = visibleLiveArenaMenu();
    if (!menu || invokeValue<int32_t>(menu, "get_Current") != 0) throw std::runtime_error("The Live Arena battle menu is not visible.");
    bool genericGacha = false;
    if (visibleLiveArenaRewardOverlay(genericGacha)) throw std::runtime_error("A Live Arena reward overlay must be closed before another reward is claimed.");
    auto* battleTab = objectField(menu, "_battleTab");
    auto* dailyQuest = objectField(battleTab, "_dailyQuest");
    const auto dailyQuestState = fieldValue<int32_t>(dailyQuest, "_currentRewardState");
    const auto dailyWins = liveArenaDailyWins(battleTab);
    int32_t claimableCount = dailyQuestState == 2 ? 1 : 0;
    for (auto* stage : dailyWins) claimableCount += rewardStateProperty(stage, "_state") == 2 ? 1 : 0;
    if (claimableCount != expectedClaimableCount) throw std::runtime_error("The Live Arena reward state changed before the claim could be submitted.");
    if (expectedClaimableCount == 4) {
        LiveArenaUiState state;
        applyLiveArenaRewardState(state, menu);
        if (!state.rewardBatchReady) throw std::runtime_error("The complete Live Arena daily reward batch is not ready.");
    }
    Il2CppObject* target = nullptr;
    const char* method = nullptr;
    if (dailyQuestState == 2) {
        target = dailyQuest;
        method = "RewardClick";
    } else {
        for (auto* stage : dailyWins) {
            if (rewardStateProperty(stage, "_state") != 2) continue;
            target = stage;
            method = "CollectClick";
            break;
        }
    }
    if (!target || !method) throw std::runtime_error("RAID exposes no claimable Live Arena daily reward.");
    Il2CppObject* exception = nullptr;
    api.runtimeInvoke(api.method(api.objectClass(target), method), target, nullptr, &exception);
    if (exception) throw std::runtime_error("RAID rejected the Live Arena reward claim.");
}

void closeLiveArenaRewardOverlay() {
    bool genericGacha = false;
    auto* overlay = visibleLiveArenaRewardOverlay(genericGacha);
    if (!overlay) throw std::runtime_error("A supported Live Arena reward overlay is not visible.");
    Il2CppObject* exception = nullptr;
    api.runtimeInvoke(api.method(api.objectClass(overlay), genericGacha ? "OnClose" : "Close"), overlay, nullptr, &exception);
    if (exception) throw std::runtime_error("RAID rejected the Live Arena reward overlay close request.");
}

Il2CppObject* requireLiveArenaBattleState() {
    auto* processor = currentBattleProcessor();
    if (!processor) throw std::runtime_error("A battle is not active.");
    auto* context = objectField(processor, "<Context>k__BackingField");
    auto* setup = objectField(context, "Setup");
    auto* state = objectField(context, "State");
    if (fieldValue<int32_t>(setup, "KindId") != 6 || fieldValue<bool>(state, "BattleFinished"))
        throw std::runtime_error("The active battle is not an unfinished Live Arena battle.");
    return state;
}

Il2CppObject* visibleBattleHud() {
    static auto* type = api.findClass("ECS.ViewModel", "BattleHUDContext");
    return visibleContext(type);
}

Il2CppObject* visibleBattleView() {
    static auto* type = api.findClass("ECS.ViewModel.BattleView", "BattleViewContext");
    return visibleContext(type);
}

Il2CppObject* requireBattleState(int32_t expectedKind, const char* arenaName) {
    auto* processor = currentBattleProcessor();
    if (!processor) throw std::runtime_error("A battle is not active.");
    auto* context = objectField(processor, "<Context>k__BackingField");
    auto* setup = objectField(context, "Setup");
    auto* state = objectField(context, "State");
    if (fieldValue<int32_t>(setup, "KindId") != expectedKind || fieldValue<bool>(state, "BattleFinished"))
        throw std::runtime_error(std::string("The active battle is not an unfinished ") + arenaName + " battle.");
    return state;
}

void setBattleAutoForKind(bool enabled, int32_t expectedKind, const char* arenaName) {
    auto* state = requireBattleState(expectedKind, arenaName);
    if (fieldValue<bool>(state, "IsAutoBattleMode") == enabled) return;
    auto* hud = visibleBattleHud();
    if (!hud) throw std::runtime_error(std::string("The ") + arenaName + " battle HUD is not visible.");
    Il2CppObject* exception = nullptr;
    api.runtimeInvoke(api.method(api.objectClass(hud), "OnChangeModeHit"), hud, nullptr, &exception);
    if (exception) throw std::runtime_error(std::string("RAID rejected the ") + arenaName + " Auto mode request.");
}

void setBattleAuto(bool enabled) {
    setBattleAutoForKind(enabled, 6, "Live Arena");
}

Il2CppObject* battleHudSkillContext(Il2CppObject* hud, int32_t skillTypeId) {
    const auto activeSkillContexts = referenceList(objectField(objectField(hud, "_activeSkills"), "_list"));
    for (size_t index = 0; index < activeSkillContexts.size(); ++index) {
        auto* skillContext = activeSkillContexts[index];
        auto* candidate = skillContext ? fieldValue<Il2CppObject*>(skillContext, "<Data>k__BackingField") : nullptr;
        if (!candidate || fieldValue<int32_t>(candidate, "TypeId") != skillTypeId) continue;
        const auto passive = invokeValue<bool>(skillContext, "get_IsPassive");
        const auto cooldown = invokeValue<int32_t>(skillContext, "get_Cooldown");
        nativeLog("Exact battle HUD skill context " + std::to_string(index) + " has cooldown "
            + std::to_string(cooldown) + (passive ? " and is passive." : " and is active."));
        if (passive || cooldown > 0) continue;
        return skillContext;
    }
    return nullptr;
}

Il2CppObject* battleHudSkill(Il2CppObject* hud, int32_t skillTypeId) {
    auto* skillContext = battleHudSkillContext(hud, skillTypeId);
    return skillContext ? fieldValue<Il2CppObject*>(skillContext, "<Data>k__BackingField") : nullptr;
}

std::string pointerText(const void* value) {
    std::ostringstream output;
    output << "0x" << std::hex << reinterpret_cast<uintptr_t>(value);
    return output.str();
}

void appendBattleDiagnosticPrimitiveFields(std::ostringstream& output, Il2CppObject* object) {
    output << '[';
    size_t count = 0;
    if (object) {
        for (auto* current = api.objectClass(object); current && count < 48; current = api.classParent(current)) {
            void* iterator = nullptr;
            while (auto* field = api.classFields(current, &iterator)) {
                if ((api.fieldFlags(field) & 0x10u) != 0) continue;
                const auto typeName = il2CppTypeName(api.fieldType(field));
                if (typeName != "System.Boolean" && typeName != "System.Int32" && typeName != "System.UInt32"
                    && typeName != "System.Int64" && typeName != "System.UInt64") continue;
                const char* name = api.fieldName(field);
                if (!name || std::char_traits<char>::length(name) > 128) continue;
                if (count++) output << ',';
                output << "{\"name\":\"" << jsonEscape(name) << "\",\"value\":";
                appendTraceFieldValue(output, object, field, typeName);
                output << '}';
                if (count >= 48) break;
            }
        }
    }
    output << ']';
}

FieldInfo* instanceFieldAtOffset(Il2CppClass* type, uint32_t wantedOffset) {
    FieldInfo* match = nullptr;
    for (auto* current = type; current; current = api.classParent(current)) {
        void* iterator = nullptr;
        while (auto* field = api.classFields(current, &iterator)) {
            if ((api.fieldFlags(field) & 0x10u) != 0 || api.fieldOffset(field) != wantedOffset) continue;
            if (match) return nullptr;
            match = field;
        }
    }
    return match;
}

struct BattleCommandGeneratorReference {
    Il2CppObject* object{};
    FieldInfo* modeField{};
};

BattleCommandGeneratorReference exactBattleCommandGenerator(Il2CppObject* mode) {
    requireObject(mode, "The visible ClientBattleMode is unavailable.");
    auto* expectedType = api.findClass("ECS.ViewModel.BattleView.BattleAccess", "ClientCommandGenerator");
    FieldInfo* generatorField = nullptr;
    size_t matches = 0;
    for (auto* current = api.objectClass(mode); current; current = api.classParent(current)) {
        void* iterator = nullptr;
        while (auto* field = api.classFields(current, &iterator)) {
            if ((api.fieldFlags(field) & 0x10u) != 0) continue;
            if (il2CppTypeName(api.fieldType(field)) != "ECS.ViewModel.BattleView.BattleAccess.ClientCommandGenerator") continue;
            ++matches;
            generatorField = field;
        }
    }
    if (matches != 1 || !generatorField || api.fieldOffset(generatorField) != 104)
        throw std::runtime_error("The build-pinned ClientCommandGenerator field was not uniquely proven.");
    Il2CppObject* generator = nullptr;
    api.fieldValue(mode, generatorField, &generator);
    requireObject(generator, "The visible ClientCommandGenerator is unavailable.");
    if (!isTypeOrSubclass(api.objectClass(generator), expectedType))
        throw std::runtime_error("The resolved command generator has an unexpected IL2CPP type.");
    return {generator, generatorField};
}

FieldInfo* requireCommandGeneratorField(Il2CppObject* generator, const char* name, const char* typeName, uint32_t offset) {
    auto* field = api.field(api.objectClass(generator), name);
    if ((api.fieldFlags(field) & 0x10u) != 0 || api.fieldOffset(field) != offset || il2CppTypeName(api.fieldType(field)) != typeName)
        throw std::runtime_error(std::string("The build-pinned ClientCommandGenerator field is invalid: ") + name);
    return field;
}

void appendMythicalCommandState(std::ostringstream& output, Il2CppObject* mode) {
    output << '{';
    try {
        const auto resolved = exactBattleCommandGenerator(mode);
        auto* generator = resolved.object;

        output << "\"available\":true,\"pointer\":\"" << pointerText(generator)
               << "\",\"class\":\"" << jsonEscape(il2CppClassName(api.objectClass(generator)))
               << "\",\"modeField\":\"" << jsonEscape(api.fieldName(resolved.modeField)) << "\",\"fields\":[";
        size_t count = 0;
        for (auto* current = api.objectClass(generator); current && count < 48; current = api.classParent(current)) {
            void* iterator = nullptr;
            while (auto* field = api.classFields(current, &iterator)) {
                if ((api.fieldFlags(field) & 0x10u) != 0) continue;
                const char* name = api.fieldName(field);
                if (!name || std::char_traits<char>::length(name) > 128) continue;
                const auto typeName = il2CppTypeName(api.fieldType(field));
                const auto offset = static_cast<uint32_t>(api.fieldOffset(field));
                if (count++) output << ',';
                output << "{\"name\":\"" << jsonEscape(name) << "\",\"type\":\"" << jsonEscape(typeName)
                       << "\",\"offset\":" << offset;
                if (typeName == "System.Boolean" || typeName == "System.Int32" || typeName == "System.UInt32"
                    || typeName == "System.Int64" || typeName == "System.UInt64") {
                    output << ",\"value\":";
                    appendTraceFieldValue(output, generator, field, typeName);
                } else if (typeName.find("System.Nullable") == 0 && typeName.find("System.Int32") != std::string::npos) {
                    struct NullableInt { bool hasValue{}; uint8_t padding[3]{}; int32_t value{}; } value;
                    api.fieldValue(generator, field, &value);
                    output << ",\"hasValue\":" << (value.hasValue ? "true" : "false")
                           << ",\"value\":" << value.value;
                } else if (offset == 32 && isReferenceType(api.fieldType(field))) {
                    Il2CppObject* reference = nullptr;
                    api.fieldValue(generator, field, &reference);
                    output << ",\"referencePresent\":" << (reference ? "true" : "false");
                    if (readable(reference, 16))
                        output << ",\"referenceClass\":\"" << jsonEscape(il2CppClassName(api.objectClass(reference))) << '"';
                }
                output << '}';
                if (count >= 48) break;
            }
        }
        output << ']';

        const auto appendPrecondition = [&](const char* label, uint32_t offset, const char* requiredType) {
            output << ",\"" << label << "\":{";
            auto* field = instanceFieldAtOffset(api.objectClass(generator), offset);
            if (!field) {
                output << "\"available\":false,\"reason\":\"field missing\"}";
                return;
            }
            const auto typeName = il2CppTypeName(api.fieldType(field));
            output << "\"name\":\"" << jsonEscape(api.fieldName(field)) << "\",\"type\":\""
                   << jsonEscape(typeName) << "\",\"offset\":" << offset;
            if (typeName != requiredType) {
                output << ",\"available\":false,\"reason\":\"type mismatch\"}";
                return;
            }
            output << ",\"available\":true,\"value\":";
            appendTraceFieldValue(output, generator, field, typeName);
            output << '}';
        };
        appendPrecondition("stateAt24", 24, "System.Int32");
        appendPrecondition("manualActiveAt56", 56, "System.Boolean");

        output << ",\"selectedSkillAt80\":{";
        auto* selectedSkillField = instanceFieldAtOffset(api.objectClass(generator), 80);
        if (!selectedSkillField) {
            output << "\"available\":false,\"reason\":\"field missing\"}";
        } else {
            const auto typeName = il2CppTypeName(api.fieldType(selectedSkillField));
            output << "\"name\":\"" << jsonEscape(api.fieldName(selectedSkillField)) << "\",\"type\":\""
                   << jsonEscape(typeName) << "\",\"offset\":80";
            if (typeName.find("System.Nullable") != 0 || typeName.find("System.Int32") == std::string::npos) {
                output << ",\"available\":false,\"reason\":\"type mismatch\"}";
            } else {
                struct NullableInt { bool hasValue{}; uint8_t padding[3]{}; int32_t value{}; } value;
                api.fieldValue(generator, selectedSkillField, &value);
                output << ",\"available\":true,\"hasValue\":" << (value.hasValue ? "true" : "false")
                       << ",\"value\":" << value.value << '}';
            }
        }
    } catch (const std::exception& exception) {
        output << "\"available\":false,\"error\":\"" << jsonEscape(exception.what()) << '"';
    }
    output << '}';
}

std::string battleUiDiagnosticSnapshot(bool includeMythicalCommandState = false) {
    auto* processor = currentBattleProcessor();
    if (!processor) return "{\"available\":false}";
    auto* context = fieldValue<Il2CppObject*>(processor, "<Context>k__BackingField");
    auto* state = context ? fieldValue<Il2CppObject*>(context, "State") : nullptr;
    auto* hud = visibleBattleHud();
    auto* view = visibleBattleView();
    if (!state || !hud || !view) return "{\"available\":false}";
    auto* activeHero = fieldValue<Il2CppObject*>(state, "ActiveHero");
    auto* canSelect = objectField(hud, "SelectSkillEnabled");
    auto* mode = objectField(view, "Mode");
    std::vector<Il2CppObject*> activeContexts;
    if (auto* activeSkills = objectField(hud, "_activeSkills"))
        if (auto* list = objectField(activeSkills, "_list")) activeContexts = referenceList(list);
    std::ostringstream output;
    output << "{\"available\":true,\"processor\":\"" << pointerText(processor) << "\",\"state\":\"" << pointerText(state)
           << "\",\"hud\":\"" << pointerText(hud) << "\",\"view\":\"" << pointerText(view)
           << "\",\"mode\":\"" << pointerText(mode) << "\",\"turn\":" << fieldValue<int32_t>(state, "CurrentTurn")
           << ",\"auto\":" << (fieldValue<bool>(state, "IsAutoBattleMode") ? "true" : "false")
           << ",\"playerFirst\":" << (fieldValue<bool>(state, "IsPlayerTeamFirst") ? "true" : "false")
           << ",\"activeHeroId\":" << (activeHero ? fieldValue<int32_t>(activeHero, "<Id>k__BackingField") + 1 : 0)
           << ",\"activeHeroTypeId\":" << (activeHero ? fieldValue<int32_t>(activeHero, "TypeId") : 0)
           << ",\"selectEnabled\":" << (canSelect && invokeValue<bool>(canSelect, "get_Value") ? "true" : "false")
           << ",\"skills\":[";
    for (size_t index = 0; index < activeContexts.size(); ++index) {
        if (index) output << ',';
        auto* skillContext = activeContexts[index];
        auto* data = skillContext ? fieldValue<Il2CppObject*>(skillContext, "<Data>k__BackingField") : nullptr;
        output << "{\"index\":" << index << ",\"context\":\"" << pointerText(skillContext)
               << "\",\"data\":\"" << pointerText(data) << "\",\"typeId\":" << (data ? fieldValue<int32_t>(data, "TypeId") : 0)
               << ",\"cooldown\":" << (skillContext ? invokeValue<int32_t>(skillContext, "get_Cooldown") : -1)
               << ",\"passive\":" << (skillContext && invokeValue<bool>(skillContext, "get_IsPassive") ? "true" : "false") << '}';
    }
    output << "],\"modeClass\":\"" << jsonEscape(il2CppClassName(mode ? api.objectClass(mode) : nullptr))
           << "\",\"hudFields\":";
    appendBattleDiagnosticPrimitiveFields(output, hud);
    output << ",\"modeFields\":";
    appendBattleDiagnosticPrimitiveFields(output, mode);
    if (includeMythicalCommandState) {
        output << ",\"commandState\":";
        appendMythicalCommandState(output, mode);
    }
    output << '}';
    return output.str();
}

struct BattleSkillSelection {
    Il2CppObject* state{};
    Il2CppObject* hud{};
    Il2CppObject* view{};
    Il2CppObject* context{};
    Il2CppObject* data{};
};

BattleSkillSelection requireBattleSkillSelection(int32_t skillTypeId, int32_t skillSlot) {
    if (skillTypeId <= 0 || skillSlot < 0 || skillSlot > 11) throw std::runtime_error("The battle skill request is invalid.");
    auto* state = requireLiveArenaBattleState();
    if (fieldValue<bool>(state, "IsAutoBattleMode")) throw std::runtime_error("A configured skill cannot be submitted while Auto mode is active.");
    const auto playerFirst = fieldValue<bool>(state, "IsPlayerTeamFirst");
    auto* playerTeam = objectField(state, playerFirst ? "FirstTeam" : "SecondTeam");
    auto* activeHero = fieldValue<Il2CppObject*>(state, "ActiveHero");
    if (!activeHero) throw std::runtime_error("RAID is not waiting for an active champion.");
    const auto activeId = fieldValue<int32_t>(activeHero, "<Id>k__BackingField");
    bool playerTurn = false;
    for (auto* hero : referenceList(objectField(playerTeam, "Heroes")))
        if (hero && fieldValue<int32_t>(hero, "<Id>k__BackingField") == activeId) { playerTurn = true; break; }
    if (!playerTurn) throw std::runtime_error("It is not the player's Live Arena turn.");

    bool skillReady = false;
    int32_t stateSlot = 0;
    for (auto* skill : referenceList(objectField(activeHero, "Skills"))) {
        if (!skill || !fieldValue<bool>(skill, "<IsHeroSkill>k__BackingField") || fieldValue<bool>(skill, "<IsHiddenSecretSkill>k__BackingField")) continue;
        if (fieldValue<int32_t>(skill, "TypeId") == skillTypeId) {
            if (stateSlot != skillSlot) throw std::runtime_error("The configured battle skill slot no longer matches RAID's active champion state.");
            if (fieldValue<int32_t>(skill, "Cooldown") != 0 || fieldValue<bool>(skill, "<Disabled>k__BackingField"))
                throw std::runtime_error("The configured battle skill is not currently usable.");
            skillReady = true;
            break;
        }
        ++stateSlot;
    }
    if (!skillReady) throw std::runtime_error("The configured battle skill is not exposed for the active champion.");
    auto* hud = visibleBattleHud();
    auto* view = visibleBattleView();
    if (!hud || !view) throw std::runtime_error("The Live Arena battle controls are not visible.");
    auto* canSelect = objectField(hud, "SelectSkillEnabled");
    if (!invokeValue<bool>(canSelect, "get_Value")) throw std::runtime_error("RAID is not ready to accept a battle skill.");
    auto* skillContext = battleHudSkillContext(hud, skillTypeId);
    if (!skillContext)
        throw std::runtime_error("The configured battle skill is not visible on RAID's battle HUD.");
    auto* skillData = fieldValue<Il2CppObject*>(skillContext, "<Data>k__BackingField");
    if (!skillData) throw std::runtime_error("RAID's visible battle skill data is unavailable.");
    return {state, hud, view, skillContext, skillData};
}

void selectBattleSkill(int32_t skillTypeId, int32_t skillSlot) {
    const auto selection = requireBattleSkillSelection(skillTypeId, skillSlot);
    nativeLog("Battle UI transaction before TrySelectSkill: " + battleUiDiagnosticSnapshot());
    Il2CppObject* exception = nullptr;
    auto* skillData = selection.data;
    void* arguments[] = {&skillData};
    auto* accepted = api.runtimeInvoke(api.method(api.objectClass(selection.hud), "TrySelectSkill", 1), selection.hud, arguments, &exception);
    if (exception) throw std::runtime_error("RAID rejected the configured battle skill.");
    if (!accepted || !*static_cast<bool*>(api.objectUnbox(accepted)))
        throw std::runtime_error("RAID did not accept the configured battle skill after the HUD stabilized.");
    nativeLog("Battle UI transaction after TrySelectSkill: " + battleUiDiagnosticSnapshot());
    nativeLog("RAID accepted the visible battle skill through BattleHUDContext.TrySelectSkill for type "
        + std::to_string(skillTypeId) + ".");
}

bool battleSkillIsTargetless(int32_t skillTypeId, int32_t skillSlot) {
    const auto selection = requireBattleSkillSelection(skillTypeId, skillSlot);
    struct NullableInt { bool hasValue{}; uint8_t padding[3]{}; int32_t value{}; };
    const auto target = fieldValue<NullableInt>(selection.data, "Targets");
    if (!target.hasValue || target.value < 0 || target.value > 11)
        throw std::runtime_error("RAID did not expose a supported target rule for the configured battle skill.");
    return target.value == 0;
}

void commitTargetlessBattleSkill(const BattleSkillSelection& selection, int32_t visibleSkillId, int32_t targetId, bool nonTargeted) {
    struct NullableInt { bool hasValue{}; uint8_t padding[3]{}; int32_t value{}; };
    const auto target = fieldValue<NullableInt>(selection.data, "Targets");
    if (!nonTargeted || !target.hasValue || target.value < 0 || target.value > 11)
        throw std::runtime_error("Only a RAID-declared non-targeted skill can use the manual command commit path.");

    const auto playerFirst = fieldValue<bool>(selection.state, "IsPlayerTeamFirst");
    auto* playerTeam = objectField(selection.state, playerFirst ? "FirstTeam" : "SecondTeam");
    auto* enemyTeam = objectField(selection.state, playerFirst ? "SecondTeam" : "FirstTeam");
    auto* activeHero = fieldValue<Il2CppObject*>(selection.state, "ActiveHero");
    const auto allies = referenceList(objectField(playerTeam, "Heroes"));
    const auto enemies = referenceList(objectField(enemyTeam, "Heroes"));
    if (!activeHero || std::find(allies.begin(), allies.end(), activeHero) == allies.end())
        throw std::runtime_error("The active Live Arena champion is not a member of the player's team.");
    const auto activeHeroId = fieldValue<int32_t>(activeHero, "<Id>k__BackingField");
    if (activeHeroId < 0) throw std::runtime_error("The active Live Arena champion identifier is invalid.");
    const auto requestedHeroId = targetId - 1;
    const auto findHero = [&](const std::vector<Il2CppObject*>& heroes) {
        return std::find_if(heroes.begin(), heroes.end(), [&](auto* hero) {
            return hero && fieldValue<int32_t>(hero, "<Id>k__BackingField") == requestedHeroId;
        });
    };
    const auto ally = findHero(allies);
    const auto enemy = findHero(enemies);
    auto* requestedHero = ally != allies.end() ? *ally : enemy != enemies.end() ? *enemy : nullptr;
    if (!requestedHero) throw std::runtime_error("The non-targeted battle skill completion target is not present in the battle state.");
    const auto alive = fieldValue<int64_t>(requestedHero, "Health") > 0;
    const auto validTarget = target.value == 0 ? requestedHero == activeHero
        : target.value == 2 || target.value == 6 || target.value == 8 ? enemy != enemies.end() && alive
        : target.value == 3 || target.value == 11 ? ally != allies.end() && !alive
        : target.value == 4 ? enemy != enemies.end() && !alive
        : target.value == 7 || target.value == 9 ? ally != allies.end() && requestedHero != activeHero && alive
        : target.value == 10 ? alive
        : ally != allies.end() && alive;
    if (!validTarget) throw std::runtime_error("The non-targeted battle skill completion target does not match RAID's target category.");

    auto* mode = objectField(selection.view, "Mode");
    const auto resolved = exactBattleCommandGenerator(mode);
    auto* generator = resolved.object;
    auto* modeTypeField = requireCommandGeneratorField(generator, "<ModeType>k__BackingField",
        "ECS.ViewModel.BattleView.BattleAccess.PlayModeType", 24);
    auto* hudStateField = requireCommandGeneratorField(generator, "<HudState>k__BackingField",
        "ECS.ViewModel.BattleHUDContext.State", 32);
    auto* manualField = requireCommandGeneratorField(generator, "IsWaitingForManualCommand", "System.Boolean", 56);
    auto* selectedField = requireCommandGeneratorField(generator, "_selectedSkillId", "System.Nullable<System.Int32>", 80);
    auto* serverField = requireCommandGeneratorField(generator, "_waitingForServerCommand", "System.Boolean", 92);

    int32_t modeType = -1;
    Il2CppObject* hudState = nullptr;
    bool waitingForManual = false;
    bool waitingForServer = false;
    NullableInt selectedSkill;
    api.fieldValue(generator, modeTypeField, &modeType);
    api.fieldValue(generator, hudStateField, &hudState);
    api.fieldValue(generator, manualField, &waitingForManual);
    api.fieldValue(generator, selectedField, &selectedSkill);
    api.fieldValue(generator, serverField, &waitingForServer);
    if (modeType < 0 || modeType > 8 || !readable(hudState, 16))
        throw std::runtime_error("RAID's manual battle command state is invalid.");
    if (!waitingForManual || waitingForServer)
        throw std::runtime_error("RAID is not ready to create a manual battle command.");
    if (!selectedSkill.hasValue || selectedSkill.value != visibleSkillId)
        throw std::runtime_error("RAID's command generator did not stage the configured battle skill.");

    auto* method = api.method(api.objectClass(generator), "SelectTargetManually", 1);
    if (il2CppMethodSignature(method) != "SelectTargetManually(System.Int32):System.Void")
        throw std::runtime_error("The build-pinned non-targeted manual command method is invalid.");
    Il2CppObject* exception = nullptr;
    auto heroId = requestedHeroId;
    void* arguments[] = {&heroId};
    api.runtimeInvoke(method, generator, arguments, &exception);
    if (exception)
        throw std::runtime_error("RAID rejected the non-targeted manual battle command: "
            + il2CppClassName(api.objectClass(exception)) + '.');
    nativeLog("RAID committed non-targeted battle skill " + std::to_string(visibleSkillId)
        + " through ClientCommandGenerator.SelectTargetManually for validated completion target " + std::to_string(requestedHeroId) + ".");
}

void selectBattleSkillThroughClick(int32_t skillTypeId, int32_t skillSlot, int32_t targetId, bool nonTargeted) {
    const auto selection = requireBattleSkillSelection(skillTypeId, skillSlot);
    auto visibleSkillId = invokeValue<int32_t>(selection.context, "get_Id");
    if (visibleSkillId != skillSlot)
        throw std::runtime_error("The visible battle skill identifier does not match the configured slot.");
    nativeLog("Battle UI transaction before visible skill press: " + battleUiDiagnosticSnapshot());
    Il2CppObject* exception = nullptr;
    int32_t press = 0;
    void* pressArguments[] = {&press};
    api.runtimeInvoke(api.method(api.objectClass(selection.context), "OnClickStateChanged", 1), selection.context, pressArguments, &exception);
    if (exception) throw std::runtime_error("RAID rejected the configured battle skill press.");
    if (fieldValue<int32_t>(selection.hud, "_currentlySelectedSkillId") != visibleSkillId)
        throw std::runtime_error("RAID did not stage the configured battle skill after the visible press.");
    nativeLog("Battle UI transaction after visible skill press: " + battleUiDiagnosticSnapshot());

    exception = nullptr;
    api.runtimeInvoke(api.method(api.objectClass(selection.context), "OnClick", 0), selection.context, nullptr, &exception);
    if (exception) throw std::runtime_error("RAID rejected the configured battle skill click.");

    exception = nullptr;
    int32_t release = 1;
    void* releaseArguments[] = {&release};
    api.runtimeInvoke(api.method(api.objectClass(selection.context), "OnClickStateChanged", 1), selection.context, releaseArguments, &exception);
    if (exception) throw std::runtime_error("RAID rejected the configured battle skill release.");
    struct NullableInt { bool hasValue{}; uint8_t padding[3]{}; int32_t value{}; };
    const auto target = fieldValue<NullableInt>(selection.data, "Targets");
    if (!target.hasValue || target.value < 0 || target.value > 11)
        throw std::runtime_error("RAID did not expose a supported target rule for the configured battle skill.");
    if (nonTargeted) commitTargetlessBattleSkill(selection, visibleSkillId, targetId, true);
    nativeLog("RAID received one complete visible press, click, and release for battle skill type "
        + std::to_string(skillTypeId) + ".");
}

bool selectBattleSkillTarget(int32_t skillTypeId, int32_t skillSlot, int32_t targetId) {
    if (skillTypeId <= 0 || skillSlot < 0 || skillSlot > 11 || targetId < 0) throw std::runtime_error("The battle skill target request is invalid.");
    auto* state = requireLiveArenaBattleState();
    if (fieldValue<bool>(state, "IsAutoBattleMode")) throw std::runtime_error("A configured skill target cannot be submitted while Auto mode is active.");
    auto* hud = visibleBattleHud();
    auto* battleView = visibleBattleView();
    if (!hud || !battleView) throw std::runtime_error("The Live Arena battle controls are not visible.");
    auto* skillData = battleHudSkill(hud, skillTypeId);
    if (!skillData) throw std::runtime_error("The selected battle skill is no longer visible on RAID's battle HUD.");
    auto* mode = objectField(battleView, "Mode");
    struct NullableInt { bool hasValue{}; uint8_t padding[3]{}; int32_t value{}; };
    const auto target = fieldValue<NullableInt>(skillData, "Targets");
    if (!target.hasValue || target.value < 0 || target.value > 11)
        throw std::runtime_error("RAID did not expose a supported target rule for the configured battle skill.");
    if (target.value == 0)
        throw std::runtime_error("A targetless battle skill must be submitted through RAID's visible skill click path.");
    const auto playerFirst = fieldValue<bool>(state, "IsPlayerTeamFirst");
    auto* allyTeam = objectField(state, playerFirst ? "FirstTeam" : "SecondTeam");
    auto* enemyTeam = objectField(state, playerFirst ? "SecondTeam" : "FirstTeam");
    auto* activeHero = fieldValue<Il2CppObject*>(state, "ActiveHero");
    const auto activeId = activeHero ? fieldValue<int32_t>(activeHero, "<Id>k__BackingField") : -1;
    const auto allies = referenceList(objectField(allyTeam, "Heroes"));
    if (!activeHero || std::find(allies.begin(), allies.end(), activeHero) == allies.end())
        throw std::runtime_error("The active Live Arena champion is not a member of the player's team.");
    std::vector<int32_t> targetIds;
    const auto addTarget = [&](int32_t id) {
        if (id >= 0 && std::find(targetIds.begin(), targetIds.end(), id) == targetIds.end()) targetIds.push_back(id);
    };
    const auto addTargets = [&](Il2CppObject* team, bool dead) {
        for (auto* hero : referenceList(objectField(team, "Heroes"))) {
            if (!hero || (fieldValue<int64_t>(hero, "Health") == 0) != dead) continue;
            const auto id = fieldValue<int32_t>(hero, "<Id>k__BackingField");
            addTarget(id);
        }
    };
    addTarget(activeId);
    addTargets(allyTeam, false);
    addTargets(enemyTeam, false);
    addTargets(allyTeam, true);
    addTargets(enemyTeam, true);
    if (targetIds.empty()) throw std::runtime_error("RAID battle state contains no candidate for the configured battle skill target.");
    const auto requestedId = targetId - 1;
    const auto requested = std::find(targetIds.begin(), targetIds.end(), requestedId);
    if (requested != targetIds.end()) std::rotate(targetIds.begin(), requested, requested + 1);
    for (const auto candidateTargetId : targetIds) {
        Il2CppObject* exception = nullptr;
        auto modelTargetId = candidateTargetId;
        void* targetArguments[] = {&modelTargetId};
        auto* accepted = api.runtimeInvoke(api.method(api.objectClass(mode), "TrySelectTarget", 1), mode, targetArguments, &exception);
        if (exception) {
            nativeLog("RAID deferred the configured battle skill target while its manual command state was stabilizing; managed exception="
                + il2CppClassName(api.objectClass(exception)) + ".");
            return false;
        }
        if (accepted && *static_cast<bool*>(api.objectUnbox(accepted))) {
            if (targetId > 0 && modelTargetId != requestedId)
                nativeLog("Configured battle target was not acceptable; RAID's first acceptable target was selected instead.");
            else if (targetId == 0)
                nativeLog("RAID accepted an automatic internal completion target for the configured battle skill.");
            return true;
        }
    }
    return false;
}

enum class MainThreadAction { LivePick, LivePickConfirm, LiveBan, LiveLeader, LiveQueue, LiveRefill, LiveReturn, LiveRewardClaim, LiveRewardClose, BattleAuto, BattleManual, BattleSkill, BattleSkillTargetless, BattleSkillClick, BattleTarget };

struct MainThreadInvocation {
    MainThreadAction action{MainThreadAction::LivePick};
    const std::vector<int32_t>* heroes{};
    int32_t slot{-1};
    int32_t skillSlot{-1};
    int32_t target{};
    int64_t expectedRevision{};
    int32_t expectedPrice{};
    int32_t expectedCount{};
    bool nonTargeted{};
    bool result{true};
    std::string error;
    std::atomic_bool completed{false};
};

std::atomic<MainThreadInvocation*> pendingMainThreadInvocation{};

LRESULT CALLBACK mainThreadHook(int code, WPARAM wParam, LPARAM lParam) {
    if (code >= 0) {
        if (auto* invocation = pendingMainThreadInvocation.exchange(nullptr)) {
            try {
                if (invocation->action == MainThreadAction::LivePick) pickLiveArena(liveArenaSelectionContext(), *invocation->heroes);
                else if (invocation->action == MainThreadAction::LivePickConfirm) { auto* context = liveArenaSelectionContext(); requireLiveArenaTurn(context, 2); confirmLiveArena(context); }
                else if (invocation->action == MainThreadAction::LiveBan) selectLiveArenaSlot(liveArenaSelectionContext(), 3, invocation->slot, "_enemySquad");
                else if (invocation->action == MainThreadAction::LiveLeader) selectLiveArenaSlot(liveArenaSelectionContext(), 4, invocation->slot, "_mySquad");
                else if (invocation->action == MainThreadAction::LiveQueue) queueLiveArena();
        else if (invocation->action == MainThreadAction::LiveRefill) refillLiveArena(invocation->expectedPrice);
                else if (invocation->action == MainThreadAction::LiveReturn) returnToLiveArena();
                else if (invocation->action == MainThreadAction::LiveRewardClaim) claimNextLiveArenaReward(invocation->target);
                else if (invocation->action == MainThreadAction::LiveRewardClose) closeLiveArenaRewardOverlay();
                else if (invocation->action == MainThreadAction::BattleAuto) setBattleAuto(true);
                else if (invocation->action == MainThreadAction::BattleManual) setBattleAuto(false);
                else if (invocation->action == MainThreadAction::BattleSkill) selectBattleSkill(invocation->slot, invocation->skillSlot);
                else if (invocation->action == MainThreadAction::BattleSkillTargetless) invocation->result = battleSkillIsTargetless(invocation->slot, invocation->skillSlot);
                else if (invocation->action == MainThreadAction::BattleSkillClick) selectBattleSkillThroughClick(invocation->slot, invocation->skillSlot, invocation->target, invocation->nonTargeted);
                else if (invocation->action == MainThreadAction::BattleTarget) invocation->result = selectBattleSkillTarget(invocation->slot, invocation->skillSlot, invocation->target);
            } catch (const std::exception& exception) {
                invocation->error = exception.what();
            } catch (...) {
                invocation->error = "The requested action failed on RAID's main thread.";
            }
            invocation->completed.store(true, std::memory_order_release);
        }
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

BOOL CALLBACK collectGameWindow(HWND window, LPARAM parameter) {
    DWORD processId = 0;
    GetWindowThreadProcessId(window, &processId);
    if (processId == GetCurrentProcessId() && IsWindowVisible(window) && GetWindow(window, GW_OWNER) == nullptr)
        static_cast<std::vector<HWND>*>(reinterpret_cast<void*>(parameter))->push_back(window);
    return TRUE;
}

bool invokeOnMainThread(MainThreadAction action, const std::vector<int32_t>* heroes = nullptr, int32_t slot = -1, int32_t target = 0, int32_t skillSlot = -1, int64_t expectedRevision = 0, int32_t expectedPrice = 0, int32_t expectedCount = 0, bool nonTargeted = false) {
    std::vector<HWND> windows;
    EnumWindows(collectGameWindow, reinterpret_cast<LPARAM>(&windows));
    if (windows.size() != 1) throw std::runtime_error("RAID does not expose exactly one visible game window.");
    const auto threadId = GetWindowThreadProcessId(windows.front(), nullptr);
    if (!threadId) throw std::runtime_error("The RAID window thread is unavailable.");

    MainThreadInvocation invocation;
    invocation.action = action;
    invocation.heroes = heroes;
    invocation.slot = slot;
    invocation.skillSlot = skillSlot;
    invocation.target = target;
    invocation.expectedRevision = expectedRevision;
    invocation.expectedPrice = expectedPrice;
    invocation.expectedCount = expectedCount;
    invocation.nonTargeted = nonTargeted;
    pendingMainThreadInvocation.store(&invocation, std::memory_order_release);
    const auto hook = SetWindowsHookExW(WH_CALLWNDPROC, mainThreadHook, selfModule, threadId);
    if (!hook) {
        pendingMainThreadInvocation.store(nullptr);
        throw std::runtime_error("The RAID main-thread dispatcher could not be installed.");
    }
    DWORD_PTR ignored = 0;
    const auto sent = SendMessageTimeoutW(windows.front(), WM_NULL, 0, 0, SMTO_ABORTIFHUNG | SMTO_BLOCK, 5000, &ignored);
    UnhookWindowsHookEx(hook);
    auto* expected = &invocation;
    pendingMainThreadInvocation.compare_exchange_strong(expected, nullptr);
    if (!sent && !invocation.completed.load(std::memory_order_acquire)) throw std::runtime_error("The RAID main thread did not respond.");
    if (!invocation.completed.load(std::memory_order_acquire)) throw std::runtime_error("The requested action did not run on RAID's main thread.");
    if (!invocation.error.empty()) throw std::runtime_error(invocation.error);
    return invocation.result;
}

std::vector<int32_t> parseLivePicks(const std::string& command) {
    const std::string prefix = "LIVE_PICK ";
    if (command.rfind(prefix, 0) != 0) throw std::runtime_error("The LIVE_PICK command is malformed.");
    std::vector<int32_t> result;
    std::unordered_set<int32_t> unique;
    std::stringstream input(command.substr(prefix.size()));
    std::string token;
    while (std::getline(input, token, ',')) {
        size_t parsed = 0;
        const auto value = std::stoll(token, &parsed);
        if (parsed != token.size() || value <= 0 || value > INT32_MAX || !unique.insert(static_cast<int32_t>(value)).second)
            throw std::runtime_error("The LIVE_PICK command contains an invalid champion identifier.");
        result.push_back(static_cast<int32_t>(value));
    }
    if (result.empty() || result.size() > 2) throw std::runtime_error("A Live Arena pick must contain one or two champions.");
    return result;
}

struct BattleSkillRequest { int32_t typeId; int32_t slot; int32_t targetId; };

BattleSkillRequest parseBattleSkill(const std::string& command, const std::string& prefix = "BATTLE_SKILL ") {
    if (command.rfind(prefix, 0) != 0) throw std::runtime_error("The BATTLE_SKILL command is malformed.");
    const auto firstSeparator = command.find(',', prefix.size());
    const auto secondSeparator = firstSeparator == std::string::npos ? std::string::npos : command.find(',', firstSeparator + 1);
    if (firstSeparator == std::string::npos || secondSeparator == std::string::npos) throw std::runtime_error("The BATTLE_SKILL command is malformed.");
    const auto parse = [&](size_t start, size_t count, const char* error) {
        const auto token = command.substr(start, count);
        size_t parsed = 0;
        const auto value = std::stoll(token, &parsed);
        if (parsed != token.size() || value < 0 || value > INT32_MAX) throw std::runtime_error(error);
        return static_cast<int32_t>(value);
    };
    const auto skillTypeId = parse(prefix.size(), firstSeparator - prefix.size(), "The BATTLE_SKILL command contains an invalid skill identifier.");
    const auto skillSlot = parse(firstSeparator + 1, secondSeparator - firstSeparator - 1, "The BATTLE_SKILL command contains an invalid skill slot.");
    const auto targetId = parse(secondSeparator + 1, std::string::npos, "The BATTLE_SKILL command contains an invalid target identifier.");
    if (skillTypeId <= 0) throw std::runtime_error("The BATTLE_SKILL command contains an invalid skill identifier.");
    if (skillSlot > 11) throw std::runtime_error("The BATTLE_SKILL command contains an unsupported skill slot.");
    return {skillTypeId, skillSlot, targetId};
}

int32_t parseLiveSlot(const std::string& command, const std::string& prefix) {
    if (command.rfind(prefix, 0) != 0) throw std::runtime_error("The Live Arena slot command is malformed.");
    const auto token = command.substr(prefix.size());
    size_t parsed = 0;
    const auto value = std::stoll(token, &parsed);
    if (parsed != token.size() || value < 0 || value > 4) throw std::runtime_error("The Live Arena slot command is outside the supported range.");
    return static_cast<int32_t>(value);
}

int32_t parseLiveRefillPrice(const std::string& command) {
    constexpr std::string_view prefix = "LIVE_REFILL ";
    if (command.rfind(prefix, 0) != 0) throw std::runtime_error("The Live Arena refill command is malformed.");
    const auto token = command.substr(prefix.size());
    size_t parsed = 0;
    const auto value = std::stoll(token, &parsed);
    if (parsed != token.size() || value < 0 || value > 10000)
        throw std::runtime_error("The Live Arena refill Gem price is outside the supported range.");
    return static_cast<int32_t>(value);
}

std::string contentHash(const std::string& json) {
    return sha256(std::vector<uint8_t>(json.begin(), json.end()));
}

void connectPipe() {
    const std::wstring path = L"\\\\.\\pipe\\ArenaDrafter-" + std::to_wstring(GetCurrentProcessId());
    nativeLog("Connecting to the current-user named pipe.");
    for (int attempt = 0; attempt < 100 && !stopping; ++attempt) {
        pipeHandle = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (pipeHandle != INVALID_HANDLE_VALUE) { nativeLog("Named pipe connected."); return; }
        const DWORD error = GetLastError();
        if (attempt == 0) nativeLog("Initial named pipe connection failed with Win32 error " + std::to_string(error) + ".");
        if (error != ERROR_PIPE_BUSY && error != ERROR_FILE_NOT_FOUND) break;
        WaitNamedPipeW(path.c_str(), 200);
    }
    throw std::runtime_error("The local named pipe is unavailable.");
}

DWORD WINAPI worker(void*) {
    try {
        nativeLog("Native probe worker started.");
        connectPipe();
        api.load();
        nativeLog("Required IL2CPP exports resolved.");
        auto* domain = api.domainGet();
        if (!domain || !api.threadAttach(domain)) throw std::runtime_error("The probe could not attach to the IL2CPP domain.");
        nativeLog("Probe thread attached to the IL2CPP domain.");
        sendLine("{\"protocol\":1,\"type\":\"hello\",\"pid\":" + std::to_string(GetCurrentProcessId()) + ",\"version\":\"11.71.0\"}");

        const auto init = readLine();
        if (init != "INIT 1") throw std::runtime_error("The first probe command must be INIT for protocol 1.");
        Il2CppObject* app = nullptr;
        for (int attempt = 0; attempt < 300 && !stopping; ++attempt) {
            try { app = appModel(); break; } catch (...) { Sleep(200); }
        }
        if (!app) throw std::runtime_error("AppModel did not initialize within 60 seconds.");
        nativeLog("AppModel resolved.");
        const auto catalog = definitions(app);
        nativeLog("Static hero catalog resolved.");
        const auto userId = fieldValue<int64_t>(app, "<UserId>k__BackingField");
        if (userId <= 0) throw std::runtime_error("The current account identifier is unavailable.");
        nativeLog("Probe initialized; waiting for WATCH.");

        bool watching = false;
        bool catalogSent = false;
        bool force = false;
        int64_t revision = 0;
        int64_t battleRevision = 0;
        std::string lastHash;
        std::string lastBattleHash;
        std::string lastLiveArenaHash;
        std::string lastLiveArenaBattleHash;
        std::string lastRewardTraceHash;
        std::string lastBattleUiDiagnostic;
        bool battleMethodInventoryLogged = false;
        bool liveArenaBattleActive = false;
        bool battleDiagnostics = false;
        bool rewardDiagnostics = false;
        bool mythicalClickTrace = false;
        uint64_t mythicalClickTraceUntil = 0;
        uint64_t liveObservationQuarantineUntil = 0;
        uint64_t battleUiDiagnosticUntil = 0;
        auto nextPoll = GetTickCount64();
        auto nextBattlePoll = GetTickCount64();
        auto nextLiveArenaPoll = GetTickCount64();
        auto nextRewardPoll = GetTickCount64();
        while (!stopping) {
            DWORD available = 0;
            if (!PeekNamedPipe(pipeHandle, nullptr, 0, nullptr, &available, nullptr)) break;
            if (available) {
                const auto command = readLine();
                if (command == "STOP") { nativeLog("STOP command received."); break; }
                if (command == "WATCH") {
                    nativeLog("WATCH command received.");
                    watching = true;
                    force = true;
                    if (!catalogSent) { sendLine(catalogSnapshot(catalog)); catalogSent = true; }
                }
                else if (command.rfind("LIVE_PICK ", 0) == 0 || command.rfind("LIVE_BAN ", 0) == 0 || command.rfind("LIVE_LEADER ", 0) == 0) {
                    try {
                        if (!watching) throw std::runtime_error("Live Arena observation is not active.");
                        if (command.rfind("LIVE_PICK ", 0) == 0) {
                            const auto picks = parseLivePicks(command);
                            invokeOnMainThread(MainThreadAction::LivePick, &picks);
                            Sleep(50);
                            invokeOnMainThread(MainThreadAction::LivePickConfirm);
                            sendAutomation("live-submitted", "Live Arena champion pick confirmed through RAID's draft flow.");
                        } else if (command.rfind("LIVE_BAN ", 0) == 0) {
                            invokeOnMainThread(MainThreadAction::LiveBan, nullptr, parseLiveSlot(command, "LIVE_BAN "));
                            sendAutomation("live-submitted", "Live Arena ban confirmed through RAID's draft flow.");
                        } else {
                            invokeOnMainThread(MainThreadAction::LiveLeader, nullptr, parseLiveSlot(command, "LIVE_LEADER "));
                            sendAutomation("live-submitted", "Live Arena leader confirmed through RAID's draft flow.");
                        }
                    } catch (const std::exception& exception) {
                        sendAutomation("live-error", exception.what());
                    }
                }
                else if (command == "LIVE_QUEUE" || command.rfind("LIVE_REFILL ", 0) == 0 || command == "LIVE_RETURN") {
                    try {
                        if (!watching) throw std::runtime_error("Live Arena observation is not active.");
                        if (command == "LIVE_QUEUE") {
                            invokeOnMainThread(MainThreadAction::LiveQueue);
                            sendAutomation("live-session-submitted", "Live Arena matchmaking requested through RAID's battle menu.");
                        } else if (command.rfind("LIVE_REFILL ", 0) == 0) {
                            invokeOnMainThread(MainThreadAction::LiveRefill, nullptr, -1, 0, -1, 0, parseLiveRefillPrice(command));
                            sendAutomation("live-session-submitted", "The visible Live Arena token refill was confirmed.");
                        } else {
                            // The manual reference proved this exact visible dialog and Close() path.
                            // Quarantine every observer before RAID tears down the result graph.
                            liveObservationQuarantineUntil = GetTickCount64() + 5000;
                            lastLiveArenaHash.clear();
                            lastLiveArenaBattleHash.clear();
                            lastBattleHash.clear();
                            lastBattleUiDiagnostic.clear();
                            try {
                                invokeOnMainThread(MainThreadAction::LiveReturn);
                            } catch (...) {
                                liveObservationQuarantineUntil = 0;
                                throw;
                            }
                            sendAutomation("live-session-submitted", "The traced Live Arena Return action was submitted.");
                            nativeLog("Live Arena observation quarantined for five seconds after the traced Return action.");
                        }
                    } catch (const std::exception& exception) {
                        if (command == "LIVE_RETURN" && std::string(exception.what()) == "The traced Live Arena result dialog is not visibly active.")
                            sendAutomation("live-deferred", exception.what());
                        else
                            sendAutomation("live-error", exception.what());
                    }
                }
                else if (command == "LIVE_REWARD_CLOSE" || command.rfind("LIVE_REWARD_CLAIM ", 0) == 0) {
                    try {
                        if (!watching) throw std::runtime_error("Live Arena observation is not active.");
                        if (command == "LIVE_REWARD_CLOSE") {
                            invokeOnMainThread(MainThreadAction::LiveRewardClose);
                            sendAutomation("live-session-submitted", "The visible Live Arena reward overlay was closed.");
                        } else {
                            const std::string prefix = "LIVE_REWARD_CLAIM ";
                            size_t parsed = 0;
                            const auto value = std::stoll(command.substr(prefix.size()), &parsed);
                            if (parsed != command.size() - prefix.size() || value < 1 || value > 4)
                                throw std::runtime_error("The LIVE_REWARD_CLAIM command is malformed.");
                            invokeOnMainThread(MainThreadAction::LiveRewardClaim, nullptr, -1, static_cast<int32_t>(value));
                            sendAutomation("live-session-submitted", "One verified Live Arena daily reward claim was submitted.");
                        }
                    } catch (const std::exception& exception) {
                        sendAutomation("live-error", exception.what());
                    }
                }
                else if (command == "BATTLE_DIAGNOSTICS START" || command == "BATTLE_DIAGNOSTICS STOP") {
                    battleDiagnostics = command == "BATTLE_DIAGNOSTICS START";
                    lastBattleUiDiagnostic.clear();
                    battleMethodInventoryLogged = false;
                    nextBattlePoll = GetTickCount64();
                    sendAutomation("battle-diagnostics", battleDiagnostics
                        ? "High-frequency manual battle diagnostics started."
                        : "High-frequency manual battle diagnostics stopped.");
                }
                else if (command == "MYTHICAL_CLICK_TRACE START" || command == "MYTHICAL_CLICK_TRACE STOP") {
                    mythicalClickTrace = command == "MYTHICAL_CLICK_TRACE START";
                    mythicalClickTraceUntil = mythicalClickTrace ? GetTickCount64() + 15000 : 0;
                    lastBattleUiDiagnostic.clear();
                    battleMethodInventoryLogged = false;
                    nextBattlePoll = GetTickCount64();
                    sendAutomation("mythical-click-trace", mythicalClickTrace
                        ? "Passive Mythical click-path trace started for 15 seconds."
                        : "Passive Mythical click-path trace stopped.");
                }
                else if (command == "REWARD_DIAGNOSTICS START" || command == "REWARD_DIAGNOSTICS STOP") {
                    rewardDiagnostics = command == "REWARD_DIAGNOSTICS START";
                    lastRewardTraceHash.clear();
                    nextRewardPoll = GetTickCount64();
                    sendAutomation("reward-diagnostics", rewardDiagnostics
                        ? "Passive Live Arena reward diagnostics started."
                        : "Passive Live Arena reward diagnostics stopped.");
                }
                else if (command == "BATTLE_AUTO" || command == "BATTLE_MANUAL"
                    || command.rfind("BATTLE_SKILL ", 0) == 0 || command.rfind("BATTLE_SKILL_CLICK ", 0) == 0) {
                    try {
                        if (!watching) throw std::runtime_error("Live Arena observation is not active.");
                        if (command == "BATTLE_AUTO" || command == "BATTLE_MANUAL") {
                            invokeOnMainThread(command == "BATTLE_AUTO" ? MainThreadAction::BattleAuto : MainThreadAction::BattleManual);
                            sendAutomation("battle-submitted", command == "BATTLE_AUTO"
                                ? "Auto mode was requested through RAID's visible Live Arena battle HUD."
                                : "Manual mode was requested for a configured opening skill through RAID's visible Live Arena battle HUD.");
                        } else {
                            const auto clickRoute = command.rfind("BATTLE_SKILL_CLICK ", 0) == 0;
                            const auto request = parseBattleSkill(command, clickRoute ? "BATTLE_SKILL_CLICK " : "BATTLE_SKILL ");
                            const auto requiresExplicitTarget = catalogRequiresExplicitTarget(catalog, request.typeId);
                            if (requiresExplicitTarget && request.targetId <= 0)
                                throw std::runtime_error("The configured single-target battle skill requires a positive target identifier.");
                            if (!requiresExplicitTarget && request.targetId <= 0)
                                throw std::runtime_error("The configured area, self, or other non-targeted battle skill requires a positive internal completion target.");
                            const auto nonTargeted = !requiresExplicitTarget;
                            nativeLog("Battle skill " + std::to_string(request.typeId) + " routed as "
                                + (nonTargeted ? "non-targeted visible click/commit" : "explicit-target skill selection") + ".");
                            invokeOnMainThread(clickRoute || nonTargeted ? MainThreadAction::BattleSkillClick : MainThreadAction::BattleSkill,
                                nullptr, request.typeId, request.targetId, request.slot, 0, 0, 0, nonTargeted);
                            battleUiDiagnosticUntil = GetTickCount64() + 5000;
                            lastBattleUiDiagnostic.clear();
                            if (requiresExplicitTarget) {
                                bool targetSelected = false;
                                for (int attempt = 0; attempt < 20 && !targetSelected; ++attempt) {
                                    Sleep(attempt == 0 ? 300 : 100);
                                    targetSelected = invokeOnMainThread(MainThreadAction::BattleTarget, nullptr, request.typeId, request.targetId, request.slot);
                                }
                                if (!targetSelected) throw std::runtime_error("RAID did not accept a legal completion target for the configured battle skill within 3 seconds.");
                            }
                            sendAutomation("battle-submitted", nonTargeted
                                ? "The non-targeted opening skill was selected through RAID's visible lifecycle and committed through its validated manual command generator."
                                : clickRoute
                                ? "The diagnostic battle skill click lifecycle and its legal target were submitted through RAID's visible Live Arena battle HUD."
                                : "The configured opening skill was submitted through RAID's visible Live Arena battle HUD.");
                        }
                    } catch (const std::exception& exception) {
                        sendAutomation("battle-error", exception.what());
                    }
                }
                else throw std::runtime_error("The probe received an unknown command.");
            }
            const auto loopNow = GetTickCount64();
            if (liveObservationQuarantineUntil != 0) {
                if (loopNow < liveObservationQuarantineUntil) {
                    Sleep(10);
                    continue;
                }
                liveObservationQuarantineUntil = 0;
                liveArenaBattleActive = false;
                force = true;
                lastHash.clear();
                lastBattleHash.clear();
                lastLiveArenaHash.clear();
                lastLiveArenaBattleHash.clear();
                lastBattleUiDiagnostic.clear();
                nextPoll = nextBattlePoll = nextLiveArenaPoll = loopNow;
                nativeLog("Live Arena observation quarantine ended; all UI identities will be resolved again.");
                sendAutomation("live-transition", "Live Arena observation resumed after the result-screen transition.");
            }
            if (mythicalClickTrace && loopNow >= mythicalClickTraceUntil) {
                mythicalClickTrace = false;
                mythicalClickTraceUntil = 0;
                sendAutomation("mythical-click-trace", "Passive Mythical click-path trace stopped after its 15-second limit.");
            }
            if (watching && (force || GetTickCount64() >= nextPoll)) {
                auto candidate = snapshot(app, catalog, 0);
                auto hash = contentHash(candidate);
                if (force || hash != lastHash) {
                    ++revision;
                    candidate = snapshot(app, catalog, revision);
                    sendLine(candidate);
                    nativeLog("Snapshot revision " + std::to_string(revision) + " sent.");
                    lastHash = hash;
                }
                force = false;
                nextPoll = GetTickCount64() + 2000;
            }
            if (watching && GetTickCount64() >= nextLiveArenaPoll) {
                auto candidate = liveArenaSnapshot(app, catalog, userId, liveArenaBattleActive);
                auto hash = contentHash(candidate);
                if (hash != lastLiveArenaHash) {
                    recordLiveArena(candidate);
                    sendLine(candidate);
                    nativeLog("Live Arena state recorded.");
                    lastLiveArenaHash = hash;
                }
                nextLiveArenaPoll = GetTickCount64() + 100;
            }
            if (watching && GetTickCount64() >= nextBattlePoll) {
                auto candidate = battleSnapshot(catalog, 0);
                auto hash = contentHash(candidate);
                if (hash != lastBattleHash) {
                    ++battleRevision;
                    auto event = battleSnapshot(catalog, battleRevision);
                    sendLine(event);
                    nativeLog("Battle revision " + std::to_string(battleRevision) + " sent.");
                    lastBattleHash = hash;
                }
                if (liveArenaBattleActive && hash != lastLiveArenaBattleHash) {
                    recordLiveArena(battleSnapshot(catalog, battleRevision));
                    lastLiveArenaBattleHash = hash;
                } else if (!liveArenaBattleActive) {
                    lastLiveArenaBattleHash.clear();
                }
                const auto now = GetTickCount64();
                const auto detailedBattleUi = battleDiagnostics || mythicalClickTrace || now <= battleUiDiagnosticUntil;
                if (detailedBattleUi) {
                    try {
                        if (!battleMethodInventoryLogged) {
                            const auto inventory = battleUiMethodInventory();
                            if (inventory != "{\"available\":false}") {
                                nativeLog("Battle UI method inventory: " + inventory);
                                battleMethodInventoryLogged = true;
                            }
                        }
                        const auto diagnostic = battleUiDiagnosticSnapshot(mythicalClickTrace);
                        if (diagnostic != lastBattleUiDiagnostic) {
                            nativeLog("Battle UI diagnostic sample: " + diagnostic);
                            lastBattleUiDiagnostic = diagnostic;
                            if (mythicalClickTrace) {
                                std::ostringstream trace;
                                trace << "{\"protocol\":1,\"type\":\"mythicalClickTrace\",\"tickMs\":" << now
                                      << ",\"sample\":" << diagnostic << '}';
                                sendLine(trace.str());
                            }
                        }
                    } catch (const std::exception& exception) {
                        const auto diagnostic = std::string("unavailable: ") + exception.what();
                        if (diagnostic != lastBattleUiDiagnostic) {
                            nativeLog("Battle UI diagnostic sample " + diagnostic);
                            lastBattleUiDiagnostic = diagnostic;
                            if (mythicalClickTrace) {
                                std::ostringstream trace;
                                trace << "{\"protocol\":1,\"type\":\"mythicalClickTrace\",\"tickMs\":" << now
                                      << ",\"sample\":{\"available\":false,\"error\":\""
                                      << jsonEscape(exception.what()) << "\"}}";
                                sendLine(trace.str());
                            }
                        }
                    }
                } else lastBattleUiDiagnostic.clear();
                nextBattlePoll = now + (detailedBattleUi ? 16 : 100);
            }
            if (watching && rewardDiagnostics && GetTickCount64() >= nextRewardPoll) {
                try {
                    auto candidate = visibleContextTraceSnapshot("rewardTrace");
                    const auto hash = contentHash(candidate);
                    if (hash != lastRewardTraceHash) {
                        sendLine(candidate);
                        lastRewardTraceHash = hash;
                        nativeLog("Reward diagnostic state recorded.");
                    }
                } catch (const std::exception& exception) {
                    rewardDiagnostics = false;
                    sendAutomation("reward-diagnostics-error", exception.what());
                }
                nextRewardPoll = GetTickCount64() + 50;
            }
            Sleep(battleDiagnostics || mythicalClickTrace || rewardDiagnostics ? 5 : 50);
        }
    } catch (const std::exception& exception) {
        sendError("PROBE_STOPPED", exception.what());
    } catch (...) {
        sendError("PROBE_STOPPED", "The native probe stopped because of an unknown error.");
    }
    stopping = true;
    nativeLog("Native probe worker is unloading.");
    if (pipeHandle != INVALID_HANDLE_VALUE) { FlushFileBuffers(pipeHandle); CloseHandle(pipeHandle); pipeHandle = INVALID_HANDLE_VALUE; }
    FreeLibraryAndExitThread(selfModule, 0);
    return 0;
}
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        selfModule = instance;
        DisableThreadLibraryCalls(instance);
        if (HANDLE thread = CreateThread(nullptr, 0, worker, nullptr, 0, nullptr)) CloseHandle(thread);
        else return FALSE;
    }
    return TRUE;
}



