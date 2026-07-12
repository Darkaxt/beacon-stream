add_library(
  BeaconAndroidStreamCore
  STATIC
    streamcore/callback_gate.cpp
    streamcore/benchmark_collector.cpp
    streamcore/certificate_pin.cpp
    streamcore/deferred_action_queue.cpp
    streamcore/grant_mapping.cpp
    streamcore/msquic_client.cpp
    streamcore/stream_core.cpp
    streamcore/surface_owner.cpp
)
target_include_directories(
  BeaconAndroidStreamCore
  PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/streamcore
)
target_link_libraries(
  BeaconAndroidStreamCore
  PUBLIC
    Beacon::StreamContracts
    Beacon::StreamProtocol
    OpenSSL
)
if(MSVC)
  target_compile_options(BeaconAndroidStreamCore PRIVATE /W4 /WX /permissive-)
else()
  target_compile_options(BeaconAndroidStreamCore PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_library(beacon_streamcore SHARED streamcore/jni_bridge.cpp)
target_link_libraries(
  beacon_streamcore
  PRIVATE
    BeaconAndroidStreamCore
    android
    log
)
target_compile_options(beacon_streamcore PRIVATE -Wall -Wextra -Wpedantic -Werror)
