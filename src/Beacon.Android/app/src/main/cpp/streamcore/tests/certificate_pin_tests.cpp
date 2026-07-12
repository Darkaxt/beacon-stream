#include "certificate_pin.h"
#include "test_failure.h"

#include <openssl/evp.h>
#include <openssl/sha.h>
#include <openssl/x509.h>

#include <array>
#include <cstddef>
#include <memory>
#include <span>
#include <vector>

namespace {

void free_openssl_bytes(unsigned char *bytes) noexcept { OPENSSL_free(bytes); }

void validates_exact_der_spki_sha256_pin() {
  std::unique_ptr<EVP_PKEY, decltype(&EVP_PKEY_free)> key(
      EVP_PKEY_Q_keygen(nullptr, nullptr, "RSA", 2048), EVP_PKEY_free);
  BEACON_TEST_REQUIRE(key != nullptr);
  std::unique_ptr<X509, decltype(&X509_free)> certificate(X509_new(), X509_free);
  BEACON_TEST_REQUIRE(certificate != nullptr);
  BEACON_TEST_REQUIRE(X509_set_version(certificate.get(), 2) == 1);
  BEACON_TEST_REQUIRE(ASN1_INTEGER_set(X509_get_serialNumber(certificate.get()), 1) == 1);
  BEACON_TEST_REQUIRE(X509_gmtime_adj(X509_getm_notBefore(certificate.get()), 0) != nullptr);
  BEACON_TEST_REQUIRE(X509_gmtime_adj(X509_getm_notAfter(certificate.get()), 3600) != nullptr);
  BEACON_TEST_REQUIRE(X509_set_pubkey(certificate.get(), key.get()) == 1);
  BEACON_TEST_REQUIRE(X509_sign(certificate.get(), key.get(), EVP_sha256()) > 0);

  unsigned char *der_buffer = nullptr;
  const int der_size = i2d_X509(certificate.get(), &der_buffer);
  BEACON_TEST_REQUIRE(der_size > 0 && der_buffer != nullptr);
  std::unique_ptr<unsigned char, decltype(&free_openssl_bytes)> der_owner(
      der_buffer, free_openssl_bytes);

  unsigned char *spki_buffer = nullptr;
  const int spki_size = i2d_PUBKEY(key.get(), &spki_buffer);
  BEACON_TEST_REQUIRE(spki_size > 0 && spki_buffer != nullptr);
  std::unique_ptr<unsigned char, decltype(&free_openssl_bytes)> spki_owner(
      spki_buffer, free_openssl_bytes);
  std::array<std::byte, 32> expected{};
  BEACON_TEST_REQUIRE(SHA256(spki_buffer, static_cast<std::size_t>(spki_size),
                 reinterpret_cast<unsigned char *>(expected.data())) != nullptr);

  const std::span<const std::byte> der(
      reinterpret_cast<const std::byte *>(der_buffer),
      static_cast<std::size_t>(der_size));
  BEACON_TEST_REQUIRE(beacon::android::streamcore::validate_der_spki_pin(der, expected));
  expected[0] ^= std::byte{1};
  BEACON_TEST_REQUIRE(!beacon::android::streamcore::validate_der_spki_pin(der, expected));
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    validates_exact_der_spki_sha256_pin();
  });
}
