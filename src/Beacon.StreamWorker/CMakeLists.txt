add_library(
  BeaconStreamWorkerPortableCore
  STATIC
    src/benchmark_source.cpp
    src/outbound_queue.cpp
    src/quic_listener.cpp
    src/quic_ticket_store.cpp
    src/audio/worker_audio_pipeline.cpp
    src/video/worker_video_pipeline.cpp
    src/worker_events.cpp
    src/worker_host.cpp
    src/worker_ipc_frame.cpp
)

target_include_directories(
  BeaconStreamWorkerPortableCore
  PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)
target_link_libraries(
  BeaconStreamWorkerPortableCore
  PUBLIC
    Beacon::StreamContracts
    Beacon::StreamProtocol
)
if(MSVC)
  target_compile_definitions(
    BeaconStreamWorkerPortableCore
    PUBLIC NOMINMAX WIN32_LEAN_AND_MEAN
  )
  target_compile_options(
    BeaconStreamWorkerPortableCore
    PRIVATE /W4 /WX /permissive-
  )
  target_link_libraries(
    BeaconStreamWorkerPortableCore
    PRIVATE bcrypt crypt32 ncrypt ntdll
  )
else()
  target_compile_options(
    BeaconStreamWorkerPortableCore
    PRIVATE -Wall -Wextra -Wpedantic -Werror
  )
  target_link_libraries(BeaconStreamWorkerPortableCore PRIVATE OpenSSL)
endif()
add_library(
  Beacon::StreamWorkerPortableCore
  ALIAS BeaconStreamWorkerPortableCore
)

if(WIN32)
  add_library(
    BeaconStreamWorkerCore
    STATIC
      src/audio/audio_media_session.cpp
      src/audio/opus_audio_encoder.cpp
      src/audio/production_audio_capabilities.cpp
      src/audio/production_audio_generation.cpp
      src/audio/wasapi_loopback_capture.cpp
      src/capture/wgc_display_capture.cpp
      src/capture/windows_wgc_capture_platform.cpp
      src/named_pipe_channel.cpp
      src/video/d3d11_video_processor.cpp
      src/video/media_rate_controller.cpp
      src/video/nvenc_h264_encoder.cpp
      src/video/production_video_capabilities.cpp
      src/video/production_video_generation.cpp
      src/video/quic_media_rate_adapter.cpp
      src/video/video_media_session.cpp
  )
  target_include_directories(
    BeaconStreamWorkerCore
    SYSTEM PRIVATE
      "${CMAKE_SOURCE_DIR}/vendor/nv-codec-headers/include"
  )
  target_link_libraries(
    BeaconStreamWorkerCore
    PUBLIC Beacon::StreamWorkerPortableCore
    PRIVATE
      d3d11
      d3dcompiler
      dwmapi
      dxgi
      ole32
      runtimeobject
      windowsapp
      opus
  )
  target_compile_options(BeaconStreamWorkerCore PRIVATE /W4 /WX /permissive-)
  beacon_use_windows_app_sdk(BeaconStreamWorkerCore)
  add_library(Beacon::StreamWorkerCore ALIAS BeaconStreamWorkerCore)

  add_executable(BeaconStreamWorker src/main.cpp)
  set_target_properties(BeaconStreamWorker PROPERTIES OUTPUT_NAME "Beacon.StreamWorker")
  target_link_libraries(BeaconStreamWorker PRIVATE Beacon::StreamWorkerCore bcrypt)
  target_compile_options(BeaconStreamWorker PRIVATE /W4 /WX /permissive-)
  beacon_use_windows_app_sdk(BeaconStreamWorker)
  add_executable(Beacon::StreamWorker ALIAS BeaconStreamWorker)
endif()
