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
set(
  BEACON_WINDOWS_APP_SDK_DIR
  "${BEACON_NATIVE_DEPENDENCIES_DIR}/windows-app-sdk"
  CACHE PATH
  "Pinned Windows App SDK packages populated by install-windows-app-sdk.ps1"
)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(WIN32)
  set(beacon_windows_app_sdk_foundation "${BEACON_WINDOWS_APP_SDK_DIR}/foundation")
  set(beacon_windows_app_sdk_interactive "${BEACON_WINDOWS_APP_SDK_DIR}/interactive-experiences")
  set(beacon_windows_app_sdk_runtime "${BEACON_WINDOWS_APP_SDK_DIR}/runtime")
  set(
    beacon_windows_app_sdk_bootstrap_dll
    "${beacon_windows_app_sdk_foundation}/runtimes/win-x64/native/Microsoft.WindowsAppRuntime.Bootstrap.dll"
  )
  foreach(required_file IN ITEMS
      "${beacon_windows_app_sdk_foundation}/include/MddBootstrap.h"
      "${beacon_windows_app_sdk_interactive}/include/Microsoft.UI.Interop.h"
      "${beacon_windows_app_sdk_runtime}/include/WindowsAppSDK-VersionInfo.h"
      "${BEACON_WINDOWS_APP_SDK_DIR}/projections/winrt/Microsoft.UI.h"
      "${beacon_windows_app_sdk_bootstrap_dll}")
    if(NOT EXISTS "${required_file}")
      message(FATAL_ERROR "Pinned Windows App SDK dependency is missing. Run scripts/install-windows-app-sdk.ps1.")
    endif()
  endforeach()

  function(beacon_use_windows_app_sdk target)
    target_include_directories(
      ${target}
      SYSTEM PRIVATE
        "${beacon_windows_app_sdk_foundation}/include"
        "${beacon_windows_app_sdk_interactive}/include"
        "${beacon_windows_app_sdk_runtime}/include"
        "${BEACON_WINDOWS_APP_SDK_DIR}/projections"
    )
    add_custom_command(
      TARGET ${target}
      POST_BUILD
      COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "${beacon_windows_app_sdk_bootstrap_dll}"
        "$<TARGET_FILE_DIR:${target}>"
      VERBATIM
    )
  endfunction()
endif()

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
  if(WIN32)
    set(QUIC_TLS_LIB "schannel" CACHE STRING "" FORCE)
  else()
    set(QUIC_TLS_LIB "quictls" CACHE STRING "" FORCE)
  endif()
  add_subdirectory("${msquic_source}" msquic EXCLUDE_FROM_ALL)
endif()

set(protobuf_source "${BEACON_NATIVE_DEPENDENCIES_DIR}/protobuf")
set(abseil_source "${BEACON_NATIVE_DEPENDENCIES_DIR}/abseil-cpp")
if(NOT EXISTS "${protobuf_source}/cmake/protobuf-generate.cmake")
  message(FATAL_ERROR "Pinned Protobuf source is missing. Bootstrap the protobuf dependency.")
endif()
if(NOT EXISTS "${abseil_source}/CMakeLists.txt")
  message(FATAL_ERROR "Pinned Abseil source is missing. Bootstrap the abseil-cpp dependency.")
endif()
if(NOT EXISTS "${BEACON_PROTOC_EXECUTABLE}")
  message(FATAL_ERROR "BEACON_PROTOC_EXECUTABLE must name the pinned protoc 32.1 host executable.")
endif()

set(ABSL_ENABLE_INSTALL OFF CACHE BOOL "" FORCE)
set(ABSL_PROPAGATE_CXX_STD ON CACHE BOOL "" FORCE)
add_subdirectory("${abseil_source}" abseil EXCLUDE_FROM_ALL)

set(WITH_PROTOC "${BEACON_PROTOC_EXECUTABLE}")
set(protobuf_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(protobuf_BUILD_CONFORMANCE OFF CACHE BOOL "" FORCE)
set(protobuf_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(protobuf_BUILD_PROTOC_BINARIES OFF CACHE BOOL "" FORCE)
set(protobuf_BUILD_LIBPROTOC OFF CACHE BOOL "" FORCE)
set(protobuf_BUILD_LIBUPB OFF CACHE BOOL "" FORCE)
set(protobuf_BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(protobuf_INSTALL OFF CACHE BOOL "" FORCE)
set(protobuf_LOCAL_DEPENDENCIES_ONLY ON CACHE BOOL "" FORCE)
set(protobuf_MSVC_STATIC_RUNTIME OFF CACHE BOOL "" FORCE)
set(protobuf_WITH_ZLIB OFF CACHE BOOL "" FORCE)
add_subdirectory("${protobuf_source}" protobuf EXCLUDE_FROM_ALL)

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
add_library(BeaconStreamContracts STATIC ${beacon_generated_contracts})
add_dependencies(BeaconStreamContracts BeaconStreamContractsGenerated)
target_include_directories(
  BeaconStreamContracts
  PUBLIC
    "${beacon_generated_contracts_dir}"
)
target_include_directories(
  BeaconStreamContracts
  SYSTEM PUBLIC
    "${protobuf_source}/src"
    "${abseil_source}"
)
target_link_libraries(BeaconStreamContracts PUBLIC protobuf::libprotobuf-lite)
add_library(Beacon::StreamContracts ALIAS BeaconStreamContracts)

add_subdirectory(../src/Beacon.StreamProtocol Beacon.StreamProtocol)

if(ANDROID)
  add_subdirectory(../src/Beacon.Android/app/src/main/cpp Beacon.Android.StreamCore)
endif()

if(WIN32 OR (UNIX AND NOT ANDROID AND BUILD_TESTING))
  add_subdirectory(../src/Beacon.StreamWorker Beacon.StreamWorker)
endif()

if(BUILD_TESTING)
  add_subdirectory(../tests/Beacon.StreamProtocol.Tests Beacon.StreamProtocol.Tests)
  if(WIN32)
    add_subdirectory(../tests/Beacon.StreamWorker.Tests Beacon.StreamWorker.Tests)
  endif()
  add_test(
    NAME BeaconStreamProtocol.GeneratedContracts
    COMMAND
      ${CMAKE_COMMAND}
      -DGENERATED_DIR=${beacon_generated_contracts_dir}
      -P ${CMAKE_CURRENT_SOURCE_DIR}/../tests/Beacon.StreamProtocol.Tests/generated_contracts_test.cmake
  )
endif()
