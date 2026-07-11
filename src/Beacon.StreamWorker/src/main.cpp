#include "beacon/worker/named_pipe_channel.h"
#include "beacon/worker/outbound_queue.h"
#include "beacon/worker/quic_listener.h"
#include "beacon/worker/worker_host.h"

#include <Windows.h>
#include <bcrypt.h>

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <thread>
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
    beacon::worker::WorkerOutboundQueue outbound;
    std::atomic_int process_result{0};
    const auto record_failure = [&process_result](int code) {
      int expected = 0;
      static_cast<void>(process_result.compare_exchange_strong(
          expected, code, std::memory_order_acq_rel));
    };
    beacon::worker::AuthorizedQuicTicketStore tickets;
    beacon::worker::QuicListener transport(arguments[4], tickets);
    transport.set_event_sink(
        [&channel, &outbound, &record_failure](
            beacon::worker::v1::WorkerIpcEnvelope event) {
          try {
            if (outbound.enqueue({std::move(event)})) {
              return;
            }
          } catch (...) {
          }
          record_failure(5);
          outbound.close();
          channel.cancel_pending_io();
        });
    beacon::worker::WorkerHost host(
        std::move(instance_id), GetCurrentProcessId(), transport, tickets);
    if (!outbound.enqueue({host.hello(), host.ready()})) {
      return 3;
    }

    std::thread command_reader([&] {
      try {
        while (!host.shutdown_requested()) {
          beacon::worker::v1::WorkerIpcEnvelope request;
          if (channel.read(request) !=
              beacon::worker::FrameDecodeStatus::success) {
            record_failure(4);
            transport.shutdown();
            outbound.close();
            channel.cancel_pending_io();
            return;
          }
          auto responses = host.dispatch(request);
          const bool shutdown_requested = host.shutdown_requested();
          const bool enqueued =
              shutdown_requested
                  ? outbound.enqueue_terminal(std::move(responses))
                  : outbound.enqueue(std::move(responses));
          if (!enqueued) {
            record_failure(5);
            transport.shutdown();
            outbound.close();
            channel.cancel_pending_io();
            return;
          }
          if (shutdown_requested) {
            return;
          }
        }
      } catch (...) {
        record_failure(6);
        transport.shutdown();
        outbound.close();
        channel.cancel_pending_io();
      }
    });

    bool write_failed = false;
    while (auto item = outbound.wait_pop()) {
      for (const auto &response : item->batch) {
        if (!channel.write(response)) {
          write_failed = true;
          break;
        }
      }
      if (write_failed) {
        record_failure(5);
        transport.shutdown();
        outbound.close();
        channel.cancel_pending_io();
        break;
      }
      if (item->kind ==
          beacon::worker::WorkerOutboundBatchKind::terminal) {
        outbound.close();
      }
    }
    channel.cancel_pending_io();
    command_reader.join();
    return process_result.load(std::memory_order_acquire);
  } catch (...) {
    return 6;
  }
}
