#pragma once

#include <cstdio>
#include <cstdlib>
#include <exception>
#include <utility>

namespace beacon::stream::testing {

struct TestFailure final {
  const char* expression;
  const char* file;
  int line;
};

[[noreturn]] inline void fail_test(const char* expression, const char* file, int line) {
  throw TestFailure{expression, file, line};
}

inline void require_test(bool condition,
                         const char* expression,
                         const char* file,
                         int line) {
  if (!condition) {
    fail_test(expression, file, line);
  }
}

template <typename Tests>
int run_tests(Tests&& tests) noexcept {
  try {
    std::forward<Tests>(tests)();
    return EXIT_SUCCESS;
  } catch (const TestFailure& failure) {
    std::fprintf(stderr, "Test requirement failed: %s (%s:%d)\n",
                 failure.expression, failure.file, failure.line);
  } catch (const std::exception& failure) {
    std::fprintf(stderr, "Unexpected test exception: %s\n", failure.what());
  } catch (...) {
    std::fputs("Unexpected non-standard test exception.\n", stderr);
  }
  std::fflush(stderr);
  return EXIT_FAILURE;
}

}  // namespace beacon::stream::testing

#define BEACON_TEST_REQUIRE(expression) \
  ::beacon::stream::testing::require_test(static_cast<bool>(expression), #expression, __FILE__, \
                                          __LINE__)
