# Beacon Media Datagram V1

Every media QUIC datagram starts with this fixed 40-byte network-order header:

```text
magic:u32 | version:u8 | media_kind:u8 | flags:u16
sequence:u64 | presentation_time_us:u64 | frame_bytes:u32
chunk_index:u16 | chunk_count:u16 | payload_offset:u32
payload_bytes:u16 | reserved:u16
```

- `magic` is `0x42535452` (`BSTR`).
- `version` is `1`.
- `media_kind` is `1` for video and `2` for future audio.
- flag bits are `0x0001` IDR, `0x0002` codec configuration, and `0x0004`
  end of access unit. Other bits are invalid in V1.
- one encoded access unit owns one `sequence`; every chunk repeats the complete encoded
  `frame_bytes` size and presentation timestamp.
- `payload_bytes` must equal the bytes following the header. `payload_offset +
  payload_bytes` must not exceed `frame_bytes`.
- `chunk_count` is nonzero and `chunk_index` is zero-based and less than `chunk_count`.
- `reserved` is zero.
- `frame_bytes` is never greater than 16 MiB.

Chunk payload size comes from MsQuic's negotiated `MaxSendLength`. Beacon does not assume an
IP MTU, add a checksum, retransmit media, or expire incomplete frames by time.
