if set_plugin_info then
    local my_info = {
        version     = "1.0",
        description = "A plugin to dissect Steam In-Home Streaming communication",
        author      = "krzys_h",
    }
    set_plugin_info(my_info)
end

-- Enum values from: https://github.com/SteamDatabase/Protobufs/blob/master/steam/stream.proto

local stream_channel = { -- k_EStreamChannel___
	[0] = "Discovery",
	[1] = "Control",
	[2] = "Stats",
}

local discovery_message = { -- k_EStreamDiscovery___
	[1] = "PingRequest",
	[2] = "PingResponse"
}

local control_message = { -- k_EStreamControl___
	[1] = "AuthenticationRequest",
	[2] = "AuthenticationResponse",
	[3] = "NegotiationInit",
	[4] = "NegotiationSetConfig",
	[5] = "NegotiationComplete",
	[6] = "ClientHandshake",
	[7] = "ServerHandshake",
	[8] = "StartNetworkTest",
	[9] = "KeepAlive",
	--[15] = "_LAST_SETUP_MESSAGE",
	[50] = "StartAudioData",
	[51] = "StopAudioData",
	[52] = "StartVideoData",
	[53] = "StopVideoData",
	[54] = "InputMouseMotion",
	[55] = "InputMouseWheel",
	[56] = "InputMouseDown",
	[57] = "InputMouseUp",
	[58] = "InputKeyDown",
	[59] = "InputKeyUp",
	[60] = "InputGamepadAttached_OBSOLETE",
	[61] = "InputGamepadEvent_OBSOLETE",
	[62] = "InputGamepadDetached_OBSOLETE",
	[63] = "ShowCursor",
	[64] = "HideCursor",
	[65] = "SetCursor",
	[66] = "GetCursorImage",
	[67] = "SetCursorImage",
	[68] = "DeleteCursor",
	[69] = "SetTargetFramerate",
	[70] = "InputLatencyTest",
	[71] = "GamepadRumble_OBSOLETE",
	[74] = "OverlayEnabled",
	[75] = "InputControllerAttached_OBSOLETE",
	[76] = "InputControllerState_OBSOLETE",
	[77] = "TriggerHapticPulse_OBSOLETE",
	[78] = "InputControllerDetached_OBSOLETE",
	[80] = "VideoDecoderInfo",
	[81] = "SetTitle",
	[82] = "SetIcon",
	[83] = "QuitRequest",
	[87] = "SetQoS",
	[88] = "InputControllerWirelessPresence_OBSOLETE",
	[89] = "SetGammaRamp",
	[90] = "VideoEncoderInfo",
	[93] = "InputControllerStateHID_OBSOLETE",
	[94] = "SetTargetBitrate",
	[95] = "SetControllerPairingEnabled_OBSOLETE",
	[96] = "SetControllerPairingResult_OBSOLETE",
	[97] = "TriggerControllerDisconnect_OBSOLETE",
	[98] = "SetActivity",
	[99] = "SetStreamingClientConfig",
	[100] = "SystemSuspend",
	[101] = "SetControllerSettings_OBSOLETE",
	[102] = "VirtualHereRequest",
	[103] = "VirtualHereReady",
	[104] = "VirtualHereShareDevice",
	[105] = "SetSpectatorMode",
	[106] = "RemoteHID",
	[107] = "StartMicrophoneData",
	[108] = "StopMicrophoneData",
	[109] = "InputText",
	[110] = "TouchConfigActive",
	[111] = "GetTouchConfigData",
	[112] = "SetTouchConfigData",
	[113] = "SaveTouchConfigLayout",
	[114] = "TouchActionSetActive",
	[115] = "GetTouchIconData",
	[116] = "SetTouchIconData",
}

local stats_message = { -- k_EStreamStats___
	[1] = "FrameEvents",
	[2] = "DebugDump",
	[3] = "LogMessage",
	[4] = "LogUploadBegin",
	[5] = "LogUploadData",
	[6] = "LogUploadComplete",
}

local data_message = { -- k_EStreamData___
	[1] = "Packet",
	[2] = "Lost",
}

-- Packet types from: my guessing

local packet_types = {
	[0] = "MTU probe",
	[1] = "SYN",
	[2] = "SYN-ACK",
	[3] = "Unreliable",
	[4] = "Unreliable (next fragment)",
	[5] = "Reliable",
	[6] = "Reliable (next fragment)",
	[7] = "Reliable ACK",
	[9] = "FIN",
}

steamstream = Proto("steamstream", "Steam Streaming")
local fields = {
	pf_flags      = ProtoField.new("Packet type flags", "steamstream.pktflags",     ftypes.UINT8,   nil, base.HEX),
	pf_flags_chk  = ProtoField.new("Has checksum",      "steamstream.has_checksum", ftypes.BOOLEAN, nil, 8, 0x80),
	pf_flags_type = ProtoField.new("Packet type",       "steamstream.type",         ftypes.UINT8,   packet_types, base.DEC, 0x0F),
	pf_retry_cnt  = ProtoField.new("Retry count",       "steamstream.retry_count",  ftypes.UINT8,   nil, base.DEC),
	pf_dir_from = ProtoField.new("From",            "steamstream.dir.from", ftypes.UINT8,  nil, base.HEX),
	pf_dir_to   = ProtoField.new("To",              "steamstream.dir.to",   ftypes.UINT8,  nil, base.HEX),
	pf_channel  = ProtoField.new("Channel",         "steamstream.channel",  ftypes.UINT8,  stream_channel, base.DEC),
	pf_fragment_count = ProtoField.new("Fragment count (excluding this)",   "steamstream.fragment_count",  ftypes.UINT16, nil, base.DEC),
	pf_fragment_num   = ProtoField.new("Fragment number (excluding first)", "steamstream.fragment_number", ftypes.UINT16, nil, base.DEC),
	pf_seq      = ProtoField.new("Sequence number", "steamstream.seq",      ftypes.UINT16, nil, base.DEC),
	pf_pkt_time = ProtoField.new("Packet timestamp (0.00001s)", "steamstream.pkt_time",      ftypes.UINT32, nil, base.DEC),
	pf_msg_type_discovery = ProtoField.new("Message type", "steamstream.msg_type.discovery", ftypes.UINT8, discovery_message, base.DEC),
	pf_msg_type_control   = ProtoField.new("Message type", "steamstream.msg_type.control",   ftypes.UINT8, control_message,   base.DEC),
	pf_msg_type_stats     = ProtoField.new("Message type", "steamstream.msg_type.stats",     ftypes.UINT8, stats_message,     base.DEC),
	pf_msg_type_data      = ProtoField.new("Message type", "steamstream.msg_type.data",      ftypes.UINT8, data_message,      base.DEC),
	pf_msg      = ProtoField.new("Message",                  "steamstream.msg",      ftypes.BYTES,  nil, base.NONE),
	pf_ack_time = ProtoField.new("ACK timestamp (0.00001s)", "steamstream.ack_time", ftypes.UINT32, nil, base.DEC),
	pf_checksum = ProtoField.new("Checksum (CRC-32C)",       "steamstream.checksum", ftypes.UINT32, nil, base.HEX),
	pf_syn_magic = ProtoField.new("Magic number",            "steamstream.syn_magic", ftypes.UINT32, nil, base.HEX),
	
	pf_mtu_type = ProtoField.new("Type",         "steamstream.mtu.type",         ftypes.UINT32, {[4] = "ping", [5] = "payload"}, base.DEC),
	pf_mtu_unk1 = ProtoField.new("Always 0x08",  "steamstream.mtu.unknown1",     ftypes.UINT8,  nil, base.HEX),
	pf_mtu_cnt  = ProtoField.new("Counter",      "steamstream.mtu.counter",      ftypes.UINT8,  nil, base.DEC),
	pf_mtu_unk2 = ProtoField.new("Always 0x10",  "steamstream.mtu.unknown2",     ftypes.UINT8,  nil, base.HEX),
	pf_mtu_data = ProtoField.new("Data",         "steamstream.mtu.data",         ftypes.BYTES,  nil, base.NONE),
	pf_mtu_size = ProtoField.new("Payload size", "steamstream.mtu.payload_size", ftypes.UINT16, nil, base.DEC),
	pf_mtu_load = ProtoField.new("Payload",      "steamstream.mtu.payload",      ftypes.BYTES,  nil, base.NONE),
	pf_mtu_zero = ProtoField.new("Always 0",     "steamstream.mtu.zero",         ftypes.UINT8,  nil, base.HEX),
}
steamstream.fields = fields
local field_has_checksum = Field.new("steamstream.has_checksum")
local field_type = Field.new("steamstream.type")
local field_seq = Field.new("steamstream.seq")
local field_fragment_count = Field.new("steamstream.fragment_count")
local field_fragment_number = Field.new("steamstream.fragment_number")
local field_retry_count = Field.new("steamstream.retry_count")
local field_mtu_type = Field.new("steamstream.mtu.type")
local field_mtu_size = Field.new("steamstream.mtu.payload_size")
function steamstream.dissector(tvbuf,pinfo,tree)
	pinfo.cols.protocol:set("STEAMSTREAM")
	local subtree = tree:add(steamstream, tvbuf(), "Steam In-Home Streaming Protocol")

	subtree:add_le(fields.pf_flags, tvbuf(0,1))
	subtree:add_le(fields.pf_flags_chk, tvbuf(0,1))
	subtree:add_le(fields.pf_flags_type, tvbuf(0,1))
	local pkttype = field_type()()
	
	subtree:add_le(fields.pf_retry_cnt, tvbuf(1,1))
	
	local dir_tree = subtree:add(tvbuf(2,2), "Direction: " .. tvbuf(2,1) .. " -> " .. tvbuf(3,1))
	dir_tree:add_le(fields.pf_dir_from, tvbuf(2,1))
	dir_tree:add_le(fields.pf_dir_to, tvbuf(3,1))
	
	local channel = tvbuf(4,1):le_uint()
	local channel_str = stream_channel[channel]
	if channel_str == nil then
		channel_str = "DataChannel" .. channel
	end
	subtree:add_le(fields.pf_channel, tvbuf(4,1))
	local packet_suffix = ""
	if pkttype == 4 or pkttype == 6 then
		subtree:add_le(fields.pf_fragment_num, tvbuf(5,2))
		packet_suffix = " [fragment #" .. field_fragment_number()() .. "]"
	else
		subtree:add_le(fields.pf_fragment_count, tvbuf(5,2))
		if field_fragment_count()() > 0 then
			packet_suffix = " [" .. field_fragment_count()() .. " fragments follow]"
		end
	end
	if field_retry_count()() > 0 then
		packet_suffix = packet_suffix .. " [RESEND #" .. field_retry_count()() .. "]"
	end
	subtree:add_le(fields.pf_seq, tvbuf(7,2))
	subtree:add_le(fields.pf_pkt_time, tvbuf(9,4))
	local packet_prefix      = "[" .. tvbuf(2,1) .. "->" .. tvbuf(3,1) .. ", SEQ:" .. string.format("%5d", field_seq()()) .. "]"
	if channel == 0 then
	     packet_prefix       = "[" .. tvbuf(2,1) .. "->" .. tvbuf(3,1) .. ",          ]"
	end
	local packet_prefix_ack  = "[" .. tvbuf(2,1) .. "->" .. tvbuf(3,1) .. ", ACK:" .. string.format("%5d", field_seq()()) .. "]"
	local checksum_len = 0
	if field_has_checksum()() then checksum_len = 4 end
	if pkttype == 0 or pkttype == 5 or pkttype == 3 then
		local msg = tvbuf(13,1):le_uint()
		local msg_str = "" .. msg
		if channel == 0 then
			subtree:add_le(fields.pf_msg_type_discovery, tvbuf(13,1))
			msg_str = discovery_message[msg]
		elseif channel == 1 then
			subtree:add_le(fields.pf_msg_type_control, tvbuf(13,1))
			msg_str = control_message[msg]
		elseif channel == 2 then
			subtree:add_le(fields.pf_msg_type_stats, tvbuf(13,1))
			msg_str = stats_message[msg]
		else
			subtree:add_le(fields.pf_msg_type_data, tvbuf(13,1))
			msg_str = data_message[msg]
		end
		local msg_len = tvbuf:len() - 14 - checksum_len
		local msg_subtree = subtree:add(fields.pf_msg, tvbuf(14,msg_len))
		if pkttype == 0 then
			msg_subtree:add_le(fields.pf_mtu_type, tvbuf(14,4))
			msg_subtree:add_le(fields.pf_mtu_unk1, tvbuf(18,1))
			msg_subtree:add_le(fields.pf_mtu_cnt,  tvbuf(19,1))
			msg_subtree:add_le(fields.pf_mtu_unk2, tvbuf(20,1))
			local data_subtree = msg_subtree:add(fields.pf_mtu_data, tvbuf(21,msg_len-7))
			if field_mtu_type()() == 5 then
				data_subtree:add_le(fields.pf_mtu_size, tvbuf(21,2))
				data_subtree:add   (fields.pf_mtu_load, tvbuf(23,msg_len-9))
				pinfo.cols.info:set(packet_prefix .. " " .. channel_str .. ", MSG " .. msg_str .. ", MTU payload " .. field_mtu_size()() .. packet_suffix)
			elseif field_mtu_type()() == 4 then
				data_subtree:add_le(fields.pf_mtu_zero, tvbuf(21,1))
				pinfo.cols.info:set(packet_prefix .. " " .. channel_str .. ", MSG " .. msg_str .. ", MTU ping" .. packet_suffix)
			else
				data_subtree:add   (tvbuf(21,msg_len-7), "Unknown - not seen yet")
			end
		elseif channel < 3 then
			if channel ~= 1 or msg == 1 or msg == 2 or msg == 6 or msg == 7 then
				pcall(function()
					Dissector.get("protobuf"):call(tvbuf(14,msg_len):tvb(), pinfo, msg_subtree)
				end)
			else
				-- TODO: implement decryption
				msg_subtree:add(tvbuf(14,msg_len), "Encrypted data")
			end
			pinfo.cols.info:set(packet_prefix .. " " .. channel_str .. ", MSG " .. msg_str .. packet_suffix)
		else
			pinfo.cols.info:set(packet_prefix .. " " .. channel_str .. ", MSG " .. msg_str .. packet_suffix)
		end
	elseif pkttype == 6 or pkttype == 4 then
		local msg_len = tvbuf:len() - 13 - checksum_len
		if msg_len > 0 then
			subtree:add(fields.pf_msg, tvbuf(13,msg_len))
		end
		pinfo.cols.info:set(packet_prefix .. " " .. channel_str .. packet_suffix)
	elseif pkttype == 7 then
		subtree:add_le(fields.pf_ack_time, tvbuf(13,4))
		pinfo.cols.info:set(packet_prefix_ack .. " " .. channel_str .. packet_suffix)
	elseif pkttype == 1 then
		subtree:add_le(fields.pf_syn_magic, tvbuf(13,4))
		pinfo.cols.info:set(packet_prefix .. " " .. channel_str .. ", SYN" .. packet_suffix)
	elseif pkttype == 2 then
		subtree:add_le(fields.pf_ack_time, tvbuf(13,4))
		pinfo.cols.info:set(packet_prefix .. " " .. channel_str .. ", SYN-ACK" .. packet_suffix)
	elseif pkttype == 9 then
		pinfo.cols.info:set(packet_prefix .. " " .. channel_str .. ", FIN" .. packet_suffix)
	else
		-- TODO: error
	end
	if field_has_checksum()() then
		subtree:add_le(fields.pf_checksum, tvbuf(tvbuf:len()-4,4))
	end
end
udp_table = DissectorTable.get("udp.port")
udp_table:add(27031, steamstream)