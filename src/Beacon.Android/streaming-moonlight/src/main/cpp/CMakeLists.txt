cmake_minimum_required(VERSION 3.22.1)
project(beacon_moonlight_core LANGUAGES C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

set(ENABLE_PROGRAMS OFF CACHE BOOL "" FORCE)
set(ENABLE_TESTING OFF CACHE BOOL "" FORCE)
set(MBEDTLS_FATAL_WARNINGS OFF CACHE BOOL "" FORCE)
set(DISABLE_PACKAGE_CONFIG_AND_INSTALL ON CACHE BOOL "" FORCE)
add_subdirectory(mbedtls EXCLUDE_FROM_ALL)

set(USE_MBEDTLS ON CACHE BOOL "" FORCE)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
add_subdirectory(moonlight-common-c EXCLUDE_FROM_ALL)

add_library(
    beacon-moonlight-core
    SHARED
    moonlight_native_core.c
    moonlight_native_connection.c)
target_include_directories(
    beacon-moonlight-core
    PRIVATE moonlight-common-c/src)
target_link_libraries(
    beacon-moonlight-core
    PRIVATE moonlight-common-c log android)
