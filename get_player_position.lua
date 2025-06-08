print("[DEBUG] get_player_position.lua started - File Output Mode")

local last_x, last_y, last_z = nil, nil, nil -- Store the last known position
local is_writing = false -- Flag to prevent overlapping writes

local function get_player_position()
    local player_manager = sdk.get_managed_singleton("app.PlayerManager")
    if player_manager == nil then return nil, nil, nil end
    
    local ret, player = pcall(player_manager.getMasterPlayer, player_manager)
    if not ret or player == nil then return nil, nil, nil end
    
    local ret, game_object = pcall(player.get_Object, player)
    if not ret or game_object == nil then return nil, nil, nil end
    
    local ret, transform = pcall(game_object.get_Transform, game_object)
    if not ret or transform == nil then return nil, nil, nil end
    
    -- Use get_Position() to get the position (prioritized)
    local ret, position = pcall(transform.get_Position, transform)
    if ret and position then
        return position.x, position.y, position.z
    end
    
    -- Method 2: getLocalPosition
    local ret, position = pcall(transform.getLocalPosition, transform)
    if ret and position then
        return position.x, position.y, position.z
    end
    
    -- Method 3: Direct field access (if getLocalPosition fails)
    local posField = transform:get_field("position")
    if posField ~= nil then
        local ret, result = pcall(posField.get_data, posField, transform)
        if ret then
            return result.x, result.y, result.z
        end
    end
    
    return nil, nil, nil
end

-- Use a separate function for file writing with retry mechanism
local function write_position_to_file(x, y, z)
    if is_writing then return false end
    is_writing = true
    
    local max_attempts = 3
    local attempt = 0
    local success = false
    
    while attempt < max_attempts and not success do
        attempt = attempt + 1
        
        local file = io.open("reframework/player_position.txt", "w")
        if file then
            local ret, err = pcall(function()
                file:write(string.format("%.2f,%.2f,%.2f", x, y, z))
                file:flush() -- Ensure data is written immediately
                file:close()
            end)
            
            if ret then
                success = true
            else
                if attempt < max_attempts then
                    -- Minimal delay before retry (just enough to give other processes a chance)
                    local start = os.clock()
                    while os.clock() - start < 0.001 do end
                end
            end
        else
            if attempt < max_attempts then
                -- Minimal delay before retry
                local start = os.clock()
                while os.clock() - start < 0.001 do end
            end
        end
    end
    
    is_writing = false
    return success
end

re.on_frame(function()
    local x, y, z = get_player_position()
    if x ~= nil and y ~= nil and z ~= nil then
        -- Only write to the file if the position has changed
        if x ~= last_x or y ~= last_y or z ~= last_z then
            if write_position_to_file(x, y, z) then
                last_x, last_y, last_z = x, y, z -- Update last known position
            end
        end
    end
end)

print("[DEBUG] get_player_position.lua script loaded and initialized")
