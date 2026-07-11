#include "beacon/worker/named_pipe_channel.h"
#include "beacon/worker/quic_listener.h"
#include "beacon/worker/worker_host.h"

#include <Windows.h>
#include <bcrypt.h>

#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace {

std::vector<std::byte> create_instance_id() {
  std::vector<std::byte> id(16);
  const auto status = BCryptGenRandom(nullptr, reinterpret_cast<PUCHAR>(id.data()),
                                      static_cast<ULONG>(id.size()),
                                      BCRYPT_USE_SYSTEM_PREFERRED_RNG);
  if (status < 0) {
    return {};
  }
  return id;
}

}  // namespace

int wmain(int argument_count, wchar_t** arguments) {
  SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
  if (argument_count != 5 || std::wstring_view(arguments[1]) != L"--pipe" ||
      std::wstring_view(arguments[3]) != L"--identity") {
    return 1;
  }

  try {
    auto channel = beacon::worker::NamedPipeChannel::connect(arguments[2]);
    auto instance_id = create_instance_id();
    if (instance_id.empty()) {
      return 2;
    }
    beacon::worker::AuthorizedQuicTicketStore tickets;
    beacon::worker::QuicListener transport(arguments[4], tickets);
    beacon::worker::WorkerHost host(
        std::move(instance_id), GetCurrentProcessId(), transport, tickets);
    if (!channel.write(host.hello()) || !channel.write(host.ready())) {
      return 3;
    }

    while (!host.shutdown_requested()) {
      beacon::worker::v1::WorkerIpcEnvelope request;
      if (channel.read(request) != beacon::worker::FrameDecodeStatus::success) {
        return 4;
      }
      for (const auto& response : host.dispatch(request)) {
        if (!channel.write(response)) {
          return 5;
        }
      }
    }
    return 0;
  } catch (...) {
    return 6;
  }
}
