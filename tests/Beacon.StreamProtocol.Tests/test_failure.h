#pragma once

#include <cstdio>
#include <cstdlib>

namespace beacon::stream::testing {

[[noreturn]] inline void fail_test(const char* expression, const char* file, int line) noexcept {
  std::fprintf(stderr, "Test requirement failed: %s (%s:%d)\n", expression, file, line);
  std::fflush(stderr);
  std::exit(EXIT_FAILURE);
}

inline void require_test(bool condition,
                         const char* expression,
                         const char* file,
                         int line) noexcept {
  if (!condition) {
    fail_test(expression, file, line);
  }
}

}  // namespace beacon::stream::testing

#define BEACON_TEST_REQUIRE(expression) \
  ::beacon::stream::testing::require_test(static_cast<bool>(expression), #expression, __FILE__, \
                                          __LINE__)
