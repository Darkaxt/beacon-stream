add_executable(BeaconStreamProtocolVersionTests version_tests.cpp)
target_link_libraries(BeaconStreamProtocolVersionTests PRIVATE Beacon::StreamProtocol)

add_test(NAME BeaconStreamProtocol.Version COMMAND BeaconStreamProtocolVersionTests)

add_executable(BeaconMediaDatagramTests media_datagram_tests.cpp)
target_link_libraries(BeaconMediaDatagramTests PRIVATE Beacon::StreamProtocol)
add_test(NAME BeaconStreamProtocol.MediaDatagram COMMAND BeaconMediaDatagramTests)

if(BEACON_WITH_MSQUIC)
  function(beacon_deploy_msquic target)
    if(WIN32)
      add_custom_command(
        TARGET ${target}
        POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
          $<TARGET_FILE:msquic>
          $<TARGET_FILE_DIR:${target}>
      )
    endif()
  endfunction()

  add_executable(BeaconMsQuicApiTests msquic_api_tests.cpp)
  target_link_libraries(BeaconMsQuicApiTests PRIVATE Beacon::StreamProtocol)
  beacon_deploy_msquic(BeaconMsQuicApiTests)
  add_test(NAME BeaconStreamProtocol.MsQuicApi COMMAND BeaconMsQuicApiTests)

  add_executable(BeaconMsQuicInteropProof msquic_interop_proof.cpp)
  target_link_libraries(BeaconMsQuicInteropProof PRIVATE Beacon::StreamProtocol)
  beacon_deploy_msquic(BeaconMsQuicInteropProof)
endif()
