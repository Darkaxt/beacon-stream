#include "certificate_pin.h"

#include <openssl/crypto.h>
#include <openssl/evp.h>
#include <openssl/sha.h>
#include <openssl/x509.h>

#include <memory>

namespace beacon::android::streamcore {
namespace {

void free_openssl_bytes(unsigned char *bytes) noexcept { OPENSSL_free(bytes); }

}  // namespace

bool decode_sha256_pin(std::string_view text,
                       std::array<std::byte, 32> &pin) noexcept {
  if (text.size() != pin.size() * 2U) {
    return false;
  }
  const auto nibble = [](char value) noexcept -> int {
    if (value >= '0' && value <= '9') return value - '0';
    if (value >= 'a' && value <= 'f') return value - 'a' + 10;
    if (value >= 'A' && value <= 'F') return value - 'A' + 10;
    return -1;
  };
  for (std::size_t index = 0; index < pin.size(); ++index) {
    const int high = nibble(text[index * 2U]);
    const int low = nibble(text[index * 2U + 1U]);
    if (high < 0 || low < 0) {
      pin.fill(std::byte{});
      return false;
    }
    pin[index] = static_cast<std::byte>((high << 4) | low);
  }
  return true;
}

bool validate_der_spki_pin(
    std::span<const std::byte> certificate_der,
    const std::array<std::byte, 32> &expected_pin) noexcept {
  if (certificate_der.empty()) {
    return false;
  }
  const auto *cursor = reinterpret_cast<const unsigned char *>(certificate_der.data());
  const auto *end = cursor + certificate_der.size();
  std::unique_ptr<X509, decltype(&X509_free)> certificate(
      d2i_X509(nullptr, &cursor, static_cast<long>(certificate_der.size())), X509_free);
  if (!certificate || cursor != end) {
    return false;
  }
  std::unique_ptr<EVP_PKEY, decltype(&EVP_PKEY_free)> public_key(
      X509_get_pubkey(certificate.get()), EVP_PKEY_free);
  if (!public_key) {
    return false;
  }
  unsigned char *spki = nullptr;
  const int spki_size = i2d_PUBKEY(public_key.get(), &spki);
  if (spki_size <= 0 || spki == nullptr) {
    return false;
  }
  std::unique_ptr<unsigned char, decltype(&free_openssl_bytes)> spki_owner(
      spki, free_openssl_bytes);
  std::array<std::byte, 32> actual{};
  if (SHA256(spki, static_cast<std::size_t>(spki_size),
             reinterpret_cast<unsigned char *>(actual.data())) == nullptr) {
    return false;
  }
  return CRYPTO_memcmp(actual.data(), expected_pin.data(), actual.size()) == 0;
}

}  // namespace beacon::android::streamcore
