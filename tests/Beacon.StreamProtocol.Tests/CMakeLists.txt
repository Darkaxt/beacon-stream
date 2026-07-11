add_executable(BeaconStreamProtocolVersionTests version_tests.cpp)
target_link_libraries(BeaconStreamProtocolVersionTests PRIVATE Beacon::StreamProtocol)

add_test(NAME BeaconStreamProtocol.Version COMMAND BeaconStreamProtocolVersionTests)

add_executable(BeaconMediaDatagramTests media_datagram_tests.cpp)
target_link_libraries(BeaconMediaDatagramTests PRIVATE Beacon::StreamProtocol)
add_test(NAME BeaconStreamProtocol.MediaDatagram COMMAND BeaconMediaDatagramTests)

add_executable(
  BeaconFakeTransportTests
  fake_transport.cpp
  fake_transport_tests.cpp
)
target_link_libraries(BeaconFakeTransportTests PRIVATE Beacon::StreamProtocol)
add_test(NAME BeaconStreamProtocol.FakeTransport COMMAND BeaconFakeTransportTests)

add_executable(
  BeaconSessionTests
  fake_transport.cpp
  session_tests.cpp
)
target_link_libraries(BeaconSessionTests PRIVATE Beacon::StreamProtocol)
add_test(NAME BeaconStreamProtocol.Session COMMAND BeaconSessionTests)

add_executable(BeaconFrameAssemblerTests frame_assembler_tests.cpp)
target_link_libraries(BeaconFrameAssemblerTests PRIVATE Beacon::StreamProtocol)
add_test(NAME BeaconStreamProtocol.FrameAssembler COMMAND BeaconFrameAssemblerTests)

if(ANDROID)
  add_executable(
    BeaconAndroidStreamCoreTests
    ../../src/Beacon.Android/app/src/main/cpp/streamcore/tests/stream_core_tests.cpp
  )
  target_link_libraries(
    BeaconAndroidStreamCoreTests
    PRIVATE
      BeaconAndroidStreamCore
      Beacon::StreamContracts
  )
  add_test(NAME BeaconAndroid.StreamCore COMMAND BeaconAndroidStreamCoreTests)

  add_executable(
    BeaconAndroidCertificatePinTests
    ../../src/Beacon.Android/app/src/main/cpp/streamcore/tests/certificate_pin_tests.cpp
  )
  target_link_libraries(
    BeaconAndroidCertificatePinTests
    PRIVATE
      BeaconAndroidStreamCore
      OpenSSL
  )
  add_test(NAME BeaconAndroid.CertificatePin COMMAND BeaconAndroidCertificatePinTests)

  add_executable(
    BeaconAndroidLifecycleTests
    ../../src/Beacon.Android/app/src/main/cpp/streamcore/tests/lifecycle_tests.cpp
  )
  target_link_libraries(
    BeaconAndroidLifecycleTests
    PRIVATE
      BeaconAndroidStreamCore
  )
  add_test(NAME BeaconAndroid.Lifecycle COMMAND BeaconAndroidLifecycleTests)
endif()

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

  add_executable(BeaconMsQuicTransportTests msquic_transport_tests.cpp)
  target_link_libraries(BeaconMsQuicTransportTests PRIVATE Beacon::StreamProtocol)
  beacon_deploy_msquic(BeaconMsQuicTransportTests)
  add_test(NAME BeaconStreamProtocol.MsQuicTransport COMMAND BeaconMsQuicTransportTests)

  add_executable(BeaconMsQuicInteropProof msquic_interop_proof.cpp)
  target_link_libraries(BeaconMsQuicInteropProof PRIVATE Beacon::StreamProtocol)
  beacon_deploy_msquic(BeaconMsQuicInteropProof)
endif()
