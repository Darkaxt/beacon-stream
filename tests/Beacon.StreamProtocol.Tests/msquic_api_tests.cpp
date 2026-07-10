#include <msquic.h>

int main() {
  const QUIC_API_TABLE *api = nullptr;
  const QUIC_STATUS status = MsQuicOpen2(&api);
  if (QUIC_FAILED(status) || api == nullptr) {
    return 1;
  }

  MsQuicClose(api);
  return 0;
}
