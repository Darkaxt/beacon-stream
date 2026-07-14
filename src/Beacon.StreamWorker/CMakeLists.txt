add_library(
  BeaconStreamWorkerCore
  STATIC
    src/benchmark_source.cpp
    src/capture/wgc_display_capture.cpp
    src/capture/windows_wgc_capture_platform.cpp
    src/named_pipe_channel.cpp
    src/outbound_queue.cpp
    src/quic_listener.cpp
    src/quic_session_protocol.cpp
    src/secure_bytes.cpp
    src/synthetic_media_source.cpp
    src/video/d3d11_video_processor.cpp
    src/video/nvenc_h264_encoder.cpp
    src/worker_events.cpp
    src/worker_host.cpp
)

target_include_directories(
  BeaconStreamWorkerCore
  PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)
target_include_directories(
  BeaconStreamWorkerCore
  SYSTEM PRIVATE
    "${CMAKE_SOURCE_DIR}/vendor/nv-codec-headers/include"
)
target_link_libraries(
  BeaconStreamWorkerCore
  PUBLIC
    Beacon::StreamContracts
    Beacon::StreamProtocol
  PRIVATE
    bcrypt
    crypt32
    ncrypt
    ntdll
    d3d11
    dxgi
    runtimeobject
    windowsapp
)
target_compile_definitions(BeaconStreamWorkerCore PUBLIC NOMINMAX WIN32_LEAN_AND_MEAN)
target_compile_options(BeaconStreamWorkerCore PRIVATE /W4 /WX /permissive-)
add_library(Beacon::StreamWorkerCore ALIAS BeaconStreamWorkerCore)

add_executable(BeaconStreamWorker src/main.cpp)
set_target_properties(BeaconStreamWorker PROPERTIES OUTPUT_NAME "Beacon.StreamWorker")
target_link_libraries(BeaconStreamWorker PRIVATE Beacon::StreamWorkerCore bcrypt)
target_compile_options(BeaconStreamWorker PRIVATE /W4 /WX /permissive-)
add_executable(Beacon::StreamWorker ALIAS BeaconStreamWorker)
