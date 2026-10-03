local MOD_TAG = "[SurvivalStatsProbe]"
local SCAN_INTERVAL_MS = 15000
local RELEVANT_INTERVAL_MS = 60000

local seen_objects = {}
local last_relevant_scan = 0

local function log(message)
    print(string.format("%s %s\n", MOD_TAG, message))
end

local function safe_call(label, callback)
    local ok, result = pcall(callback)
    if ok then
        return result
    end

    log(string.format("%s failed: %s", label, tostring(result)))
    return nil
end

local function value_to_string(value)
    if value == nil then
        return "nil"
    end

    local lua_type = type(value)
    if lua_type == "string" or lua_type == "number" or lua_type == "boolean" then
        return tostring(value)
    end

    local rendered = safe_call("render value", function()
        if value.IsValid and value:IsValid() and value.GetFullName then
            return value:GetFullName()
        end
        return tostring(value)
    end)

    return rendered or string.format("<%s>", lua_type)
end

local function is_relevant(name)
    local lower = string.lower(name)
    local needles = {
        "surviv", "minute", "time", "stat", "kill", "death", "puppet",
        "animal", "distance", "travel", "shot", "head", "lock", "loot",
        "fish", "profile", "user", "steam", "player", "prisoner"
    }

    for _, needle in ipairs(needles) do
        if string.find(lower, needle, 1, true) then
            return true
        end
    end

    return false
end

local function walk_class_hierarchy(object, property_callback, function_callback)
    local current = safe_call("GetClass", function() return object:GetClass() end)
    local visited = {}

    while current and current:IsValid() do
        local address = current:GetAddress()
        if visited[address] then
            break
        end
        visited[address] = true

        local class_name = safe_call("class GetFullName", function() return current:GetFullName() end)
        log("class: " .. tostring(class_name))

        if property_callback then
            safe_call("ForEachProperty", function()
                current:ForEachProperty(function(property)
                    property_callback(property)
                    return false
                end)
            end)
        end

        if function_callback then
            safe_call("ForEachFunction", function()
                current:ForEachFunction(function(func)
                    function_callback(func)
                    return false
                end)
            end)
        end

        current = safe_call("GetSuperStruct", function() return current:GetSuperStruct() end)
    end
end

local function inspect_object(object)
    if not object or not object:IsValid() then
        return
    end

    local full_name = safe_call("GetFullName", function() return object:GetFullName() end) or "<unknown>"
    local address = object:GetAddress()
    log(string.format("NEW handler 0x%X: %s", address, full_name))

    local outer = safe_call("GetOuter", function() return object:GetOuter() end)
    if outer and outer:IsValid() then
        log("outer: " .. tostring(outer:GetFullName()))
    end

    walk_class_hierarchy(object,
        function(property)
            local property_name = property:GetFName():ToString()
            local property_type = safe_call("property GetClass", function()
                return property:GetClass():GetFName():ToString()
            end) or "unknown"
            local value = safe_call("read property " .. property_name, function()
                return object:GetPropertyValue(property_name)
            end)
            log(string.format("property %s (%s) = %s", property_name, property_type, value_to_string(value)))
        end,
        function(func)
            local function_name = func:GetFName():ToString()
            log("function " .. function_name)
        end)
end

local function log_relevant_values(object)
    if not object or not object:IsValid() then
        return
    end

    local object_name = safe_call("GetFullName", function() return object:GetFullName() end) or "<unknown>"
    walk_class_hierarchy(object, function(property)
        local property_name = property:GetFName():ToString()
        if is_relevant(property_name) then
            local value = safe_call("read relevant property " .. property_name, function()
                return object:GetPropertyValue(property_name)
            end)
            log(string.format("RELEVANT %s :: %s = %s", object_name, property_name, value_to_string(value)))
        end
    end, nil)
end

local function find_handlers()
    local handlers = {}
    local addresses = {}

    for _, class_name in ipairs({ "BP_SurvivalStatsHandler_C", "SurvivalStatsHandler" }) do
        local found = safe_call("FindAllOf(" .. class_name .. ")", function()
            return FindAllOf(class_name)
        end)

        if found then
            for _, object in pairs(found) do
                if object and object:IsValid() then
                    local address = object:GetAddress()
                    if not addresses[address] then
                        addresses[address] = true
                        table.insert(handlers, object)
                    end
                end
            end
        end
    end

    return handlers
end


local major, minor, hotfix = UE4SS.GetVersion()
log(string.format("loaded on UE4SS %d.%d.%d; read-only scan active", major, minor, hotfix))

LoopAsync(SCAN_INTERVAL_MS, function()
    local handlers = find_handlers()

    if #handlers == 0 then
        log("no live SurvivalStatsHandler instance found")
        return false
    end

    for _, object in ipairs(handlers) do
        local address = object:GetAddress()
        if not seen_objects[address] then
            seen_objects[address] = true
            inspect_object(object)
        end
    end

    local now = os.time()
    if now - last_relevant_scan >= (RELEVANT_INTERVAL_MS / 1000) then
        last_relevant_scan = now
        for _, object in ipairs(handlers) do
            log_relevant_values(object)
        end
    end

    return false
end)

