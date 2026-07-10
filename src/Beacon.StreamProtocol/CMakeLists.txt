add_library(BeaconStreamProtocol INTERFACE)

target_include_directories(
  BeaconStreamProtocol
  INTERFACE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

add_library(Beacon::StreamProtocol ALIAS BeaconStreamProtocol)

if(MSVC)
  target_compile_options(BeaconStreamProtocol INTERFACE /W4 /WX /permissive-)
else()
  target_compile_options(BeaconStreamProtocol INTERFACE -Wall -Wextra -Wpedantic -Werror)
endif()

if(BEACON_WITH_MSQUIC)
  target_include_directories(
    BeaconStreamProtocol
    SYSTEM INTERFACE
      ${BEACON_NATIVE_DEPENDENCIES_DIR}/msquic/src/inc
  )
  target_link_libraries(BeaconStreamProtocol INTERFACE msquic)
endif()
