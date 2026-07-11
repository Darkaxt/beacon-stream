add_library(
  BeaconStreamWorkerCore
  STATIC
    src/named_pipe_channel.cpp
    src/worker_host.cpp
)

target_include_directories(
  BeaconStreamWorkerCore
  PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)
target_link_libraries(
  BeaconStreamWorkerCore
  PUBLIC
    Beacon::StreamContracts
    Beacon::StreamProtocol
)
target_compile_definitions(BeaconStreamWorkerCore PUBLIC NOMINMAX WIN32_LEAN_AND_MEAN)
target_compile_options(BeaconStreamWorkerCore PRIVATE /W4 /WX /permissive-)
add_library(Beacon::StreamWorkerCore ALIAS BeaconStreamWorkerCore)

add_executable(BeaconStreamWorker src/main.cpp)
set_target_properties(BeaconStreamWorker PROPERTIES OUTPUT_NAME "Beacon.StreamWorker")
target_link_libraries(BeaconStreamWorker PRIVATE Beacon::StreamWorkerCore bcrypt)
target_compile_options(BeaconStreamWorker PRIVATE /W4 /WX /permissive-)
add_executable(Beacon::StreamWorker ALIAS BeaconStreamWorker)
