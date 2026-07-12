add_library(
  BeaconStreamProtocol
  STATIC
    src/benchmark_datagram.cpp
    src/frame_assembler.cpp
    src/media_datagram.cpp
    src/msquic_transport.cpp
    src/session.cpp
)

target_include_directories(
  BeaconStreamProtocol
  PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

add_library(Beacon::StreamProtocol ALIAS BeaconStreamProtocol)

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
