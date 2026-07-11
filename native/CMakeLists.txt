cmake_minimum_required(VERSION 3.25)

project(BeaconNative LANGUAGES C CXX)

include(CTest)

option(BEACON_WITH_MSQUIC "Build the pinned MsQuic transport foundation" OFF)
set(
  BEACON_NATIVE_DEPENDENCIES_DIR
  "${CMAKE_CURRENT_SOURCE_DIR}/_deps"
  CACHE PATH
  "Directory populated by bootstrap-native-dependencies.ps1"
)
set(
  BEACON_PROTOC_EXECUTABLE
  ""
  CACHE FILEPATH
  "Pinned host protoc executable from Google.Protobuf.Tools 3.32.1"
)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(BEACON_WITH_MSQUIC)
  set(msquic_source "${BEACON_NATIVE_DEPENDENCIES_DIR}/msquic")
  if(NOT EXISTS "${msquic_source}/CMakeLists.txt")
    message(FATAL_ERROR "Pinned MsQuic source is missing. Run scripts/bootstrap-native-dependencies.ps1 -Name msquic.")
  endif()

  set(QUIC_BUILD_TOOLS OFF CACHE BOOL "" FORCE)
  set(QUIC_BUILD_TEST OFF CACHE BOOL "" FORCE)
  set(QUIC_BUILD_PERF OFF CACHE BOOL "" FORCE)
  set(QUIC_BUILD_SHARED ON CACHE BOOL "" FORCE)
  set(QUIC_ENABLE_LOGGING OFF CACHE BOOL "" FORCE)
  if(ANDROID)
    set(QUIC_TLS_LIB "quictls" CACHE STRING "" FORCE)
  else()
    set(QUIC_TLS_LIB "schannel" CACHE STRING "" FORCE)
  endif()
  add_subdirectory("${msquic_source}" msquic EXCLUDE_FROM_ALL)
endif()

set(protobuf_source "${BEACON_NATIVE_DEPENDENCIES_DIR}/protobuf")
if(NOT EXISTS "${protobuf_source}/cmake/protobuf-generate.cmake")
  message(FATAL_ERROR "Pinned Protobuf source is missing. Bootstrap the protobuf dependency.")
endif()
if(NOT EXISTS "${BEACON_PROTOC_EXECUTABLE}")
  message(FATAL_ERROR "BEACON_PROTOC_EXECUTABLE must name the pinned protoc 32.1 host executable.")
endif()

set(protobuf_PROTOC_EXE "${BEACON_PROTOC_EXECUTABLE}")
include("${protobuf_source}/cmake/protobuf-generate.cmake")

set(beacon_contracts_dir "${CMAKE_CURRENT_SOURCE_DIR}/../contracts")
set(beacon_generated_contracts_dir "${CMAKE_CURRENT_BINARY_DIR}/generated/contracts")
file(MAKE_DIRECTORY "${beacon_generated_contracts_dir}")
protobuf_generate(
  LANGUAGE cpp
  OUT_VAR beacon_generated_contracts
  PROTOS
    "${beacon_contracts_dir}/worker_ipc.proto"
    "${beacon_contracts_dir}/stream_control.proto"
  IMPORT_DIRS "${beacon_contracts_dir}"
  PROTOC_OUT_DIR "${beacon_generated_contracts_dir}"
  PROTOC_EXE "${BEACON_PROTOC_EXECUTABLE}"
)
add_custom_target(BeaconStreamContractsGenerated ALL DEPENDS ${beacon_generated_contracts})

add_subdirectory(../src/Beacon.StreamProtocol Beacon.StreamProtocol)

if(BUILD_TESTING)
  add_subdirectory(../tests/Beacon.StreamProtocol.Tests Beacon.StreamProtocol.Tests)
  add_test(
    NAME BeaconStreamProtocol.GeneratedContracts
    COMMAND
      ${CMAKE_COMMAND}
      -DGENERATED_DIR=${beacon_generated_contracts_dir}
      -P ${CMAKE_CURRENT_SOURCE_DIR}/../tests/Beacon.StreamProtocol.Tests/generated_contracts_test.cmake
  )
endif()
