add_library(
  BeaconStreamProtocol
  STATIC
    src/audio_media_packetizer.cpp
    src/benchmark_datagram.cpp
    src/frame_assembler.cpp
    src/media_datagram.cpp
    src/msquic_transport.cpp
    src/secure_bytes.cpp
    src/server_session_protocol.cpp
    src/session.cpp
    src/video_media_packetizer.cpp
)

target_include_directories(
  BeaconStreamProtocol
  PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

add_library(Beacon::StreamProtocol ALIAS BeaconStreamProtocol)

target_link_libraries(BeaconStreamProtocol PUBLIC Beacon::StreamContracts)

if(MSVC)
  target_compile_options(BeaconStreamProtocol PUBLIC /W4 /WX /permissive-)
else()
  target_compile_options(BeaconStreamProtocol PUBLIC -Wall -Wextra -Wpedantic -Werror)
endif()

if(BEACON_WITH_MSQUIC)
  target_include_directories(
    BeaconStreamProtocol
    SYSTEM PUBLIC
      ${BEACON_NATIVE_DEPENDENCIES_DIR}/msquic/src/inc
  )
  target_link_libraries(BeaconStreamProtocol PUBLIC msquic)
endif()
