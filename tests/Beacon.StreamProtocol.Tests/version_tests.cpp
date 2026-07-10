#include <beacon/stream/version.h>

#include <string_view>

static_assert(beacon::stream::protocol_version == 1);
static_assert(std::string_view {beacon::stream::alpn} == "beacon-stream/1");

int main() { return 0; }
