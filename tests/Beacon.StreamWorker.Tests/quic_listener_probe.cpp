#include "beacon/worker/quic_listener.h"

#include <cstdint>
#include <cstdio>
#include <string>

int wmain(int argument_count, wchar_t** arguments) {
  if (argument_count != 2) {
    return 64;
  }

  beacon::worker::AuthorizedQuicTicketStore tickets;
  beacon::worker::QuicListener listener(arguments[1], tickets);
  if (listener.configure_listener("not-an-address", 0)) {
    return 1;
  }
  if (!listener.configure_listener("127.0.0.1", 0)) {
    return 2;
  }
  if (!listener.open_connection()) {
    std::fprintf(stderr, "failure=%u platform_error=%llu\n",
                 static_cast<unsigned int>(listener.failure()),
                 static_cast<unsigned long long>(listener.platform_error()));
    return 3;
  }
  const std::uint16_t port = listener.local_port();
  if (port == 0) {
    return 4;
  }
  std::printf("BEACON_QUIC_LISTENER_READY %u\n", static_cast<unsigned int>(port));
  listener.close_connection();
  listener.shutdown();
  return 0;
}
