#include "access_unit_vector.h"
#include "hosted_emulator_endpoint_server.h"

#include <cstdio>
#include <filesystem>
#include <optional>
#include <string_view>
#include <utility>

namespace {

struct Arguments {
  std::filesystem::path certificate;
  std::filesystem::path private_key;
  std::filesystem::path vector;
};

std::optional<Arguments> parse_arguments(int argc, char **argv) {
  if (argc != 7) {
    return std::nullopt;
  }
  Arguments result;
  for (int index = 1; index < argc; index += 2) {
    const std::string_view option{argv[index]};
    const std::filesystem::path value{argv[index + 1]};
    if (value.empty()) {
      return std::nullopt;
    }
    if (option == "--certificate" && result.certificate.empty()) {
      result.certificate = value;
    } else if (option == "--private-key" && result.private_key.empty()) {
      result.private_key = value;
    } else if (option == "--vector" && result.vector.empty()) {
      result.vector = value;
    } else {
      return std::nullopt;
    }
  }
  if (result.certificate.empty() || result.private_key.empty() ||
      result.vector.empty()) {
    return std::nullopt;
  }
  return result;
}

} // namespace

int main(int argc, char **argv) {
  const auto arguments = parse_arguments(argc, argv);
  if (!arguments.has_value()) {
    std::fprintf(stderr,
                 "usage: %s --certificate <pem> --private-key <pem> "
                 "--vector <bau>\n",
                 argv[0]);
    return 64;
  }

  auto vector =
      beacon::stream::testing::load_access_unit_vector(arguments->vector);
  if (vector.error != beacon::stream::testing::AccessUnitVectorError::none) {
    std::fprintf(stderr, "BEACON_HOSTED_ENDPOINT_FAILED vector_%u\n",
                 static_cast<unsigned int>(vector.error));
    return 65;
  }

  beacon::stream::testing::HostedEmulatorEndpointServer server({
      .certificate_path = arguments->certificate,
      .private_key_path = arguments->private_key,
      .access_units = std::move(vector.access_units),
  });
  return server.run();
}
