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

add_subdirectory(../src/Beacon.StreamProtocol Beacon.StreamProtocol)

if(BUILD_TESTING)
  add_subdirectory(../tests/Beacon.StreamProtocol.Tests Beacon.StreamProtocol.Tests)
endif()
