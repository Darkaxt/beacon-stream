#include "beacon/worker/video/d3d11_video_processor.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <cstdint>
#include <memory>
#include <utility>

namespace {

using beacon::worker::capture::CapturedD3d11Frame;
using beacon::worker::capture::D3d11Texture;
using beacon::worker::video::calculate_video_processor_layout;
using beacon::worker::video::D3d11VideoProcessor;
using beacon::worker::video::D3d11VideoProcessorConfiguration;
using beacon::worker::video::D3d11VideoProcessorFailure;
using beacon::worker::video::D3d11VideoProcessorLayout;
using beacon::worker::video::D3d11VideoProcessorPlan;
using beacon::worker::video::ID3d11VideoProcessorPlatform;
using beacon::worker::video::VideoColorMatrix;
using beacon::worker::video::VideoPixelFormat;
using beacon::worker::video::VideoRange;

class FakeTexture final : public D3d11Texture {
 public:
  explicit FakeTexture(void* value) : value_(value) {}

  [[nodiscard]] void* native_texture() const noexcept override {
    return value_;
  }

 private:
  void* value_{};
};

class FakePlatform final : public ID3d11VideoProcessorPlatform {
 public:
  void* identity{reinterpret_cast<void*>(0x1000)};
  D3d11VideoProcessorFailure configure_result{D3d11VideoProcessorFailure::none};
  D3d11VideoProcessorFailure blit_result{D3d11VideoProcessorFailure::none};
  bool output_creation_succeeds{true};
  int identity_count{};
  int configure_count{};
  int create_count{};
  int blit_count{};
  int reset_count{};
  D3d11VideoProcessorConfiguration configuration{};
  D3d11VideoProcessorLayout layout{};

  [[nodiscard]] void* device_identity(const D3d11Texture&) noexcept override {
    ++identity_count;
    return identity;
  }

  [[nodiscard]] D3d11VideoProcessorFailure configure(
      const D3d11Texture&,
      const D3d11VideoProcessorConfiguration& value) noexcept override {
    ++configure_count;
    configuration = value;
    return configure_result;
  }

  [[nodiscard]] std::shared_ptr<D3d11Texture> create_output_texture() noexcept
      override {
    ++create_count;
    if (!output_creation_succeeds) {
      return {};
    }
    return std::make_shared<FakeTexture>(reinterpret_cast<void*>(
        static_cast<std::uintptr_t>(0x2000 + create_count)));
  }

  [[nodiscard]] D3d11VideoProcessorFailure blit(
      const D3d11Texture&, D3d11Texture&,
      const D3d11VideoProcessorLayout& value) noexcept override {
    ++blit_count;
    layout = value;
    return blit_result;
  }

  void reset() noexcept override { ++reset_count; }
};

CapturedD3d11Frame frame(std::uint32_t width = 2560,
                         std::uint32_t height = 1600,
                         std::int64_t timestamp = 42) {
  return {
      .texture = std::make_shared<FakeTexture>(reinterpret_cast<void*>(0x3000)),
      .width = width,
      .height = height,
      .qpc_timestamp = timestamp,
  };
}

D3d11VideoProcessorPlan plan(std::uint32_t width = 2560,
                             std::uint32_t height = 1600) {
  return {
      .output_width = width,
      .output_height = height,
      .frame_rate_numerator = 120,
      .frame_rate_denominator = 1,
  };
}

void layout_preserves_the_full_source_and_centers_the_destination() {
  const auto exact = calculate_video_processor_layout(2560, 1600, 2560, 1600);
  BEACON_TEST_REQUIRE(exact.has_value());
  BEACON_TEST_REQUIRE(exact->source.left == 0);
  BEACON_TEST_REQUIRE(exact->source.top == 0);
  BEACON_TEST_REQUIRE(exact->source.right == 2560);
  BEACON_TEST_REQUIRE(exact->source.bottom == 1600);
  BEACON_TEST_REQUIRE(exact->destination == exact->source);

  const auto wide = calculate_video_processor_layout(1920, 1080, 2560, 1600);
  BEACON_TEST_REQUIRE(wide.has_value());
  BEACON_TEST_REQUIRE(wide->source.right == 1920);
  BEACON_TEST_REQUIRE(wide->source.bottom == 1080);
  BEACON_TEST_REQUIRE(wide->destination.left == 0);
  BEACON_TEST_REQUIRE(wide->destination.top == 80);
  BEACON_TEST_REQUIRE(wide->destination.right == 2560);
  BEACON_TEST_REQUIRE(wide->destination.bottom == 1520);

  const auto narrow = calculate_video_processor_layout(1280, 1024, 2560, 1600);
  BEACON_TEST_REQUIRE(narrow.has_value());
  BEACON_TEST_REQUIRE(narrow->destination.left == 280);
  BEACON_TEST_REQUIRE(narrow->destination.top == 0);
  BEACON_TEST_REQUIRE(narrow->destination.right == 2280);
  BEACON_TEST_REQUIRE(narrow->destination.bottom == 1600);

  const auto chroma_aligned =
      calculate_video_processor_layout(1000, 1000, 1002, 1000);
  BEACON_TEST_REQUIRE(chroma_aligned.has_value());
  BEACON_TEST_REQUIRE(chroma_aligned->destination.left == 0);
  BEACON_TEST_REQUIRE(chroma_aligned->destination.right == 1000);
}

void invalid_dimensions_fail_before_touching_the_platform() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  D3d11VideoProcessor processor{std::move(platform)};

  auto invalid_frame = frame(0, 1600);
  BEACON_TEST_REQUIRE(!processor.convert(invalid_frame, plan()).has_value());
  BEACON_TEST_REQUIRE(processor.failure() ==
                      D3d11VideoProcessorFailure::invalid_frame);

  auto invalid_plan = plan(2559, 1600);
  BEACON_TEST_REQUIRE(!processor.convert(frame(), invalid_plan).has_value());
  BEACON_TEST_REQUIRE(processor.failure() ==
                      D3d11VideoProcessorFailure::invalid_plan);
  BEACON_TEST_REQUIRE(observed->identity_count == 0);
  BEACON_TEST_REQUIRE(observed->configure_count == 0);
}

void first_conversion_requests_the_exact_sdr_nv12_contract() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  D3d11VideoProcessor processor{std::move(platform)};

  const auto converted = processor.convert(frame(1920, 1080, 99), plan());

  BEACON_TEST_REQUIRE(converted.has_value());
  BEACON_TEST_REQUIRE(processor.failure() == D3d11VideoProcessorFailure::none);
  BEACON_TEST_REQUIRE(observed->configure_count == 1);
  BEACON_TEST_REQUIRE(observed->configuration.input_width == 1920);
  BEACON_TEST_REQUIRE(observed->configuration.input_height == 1080);
  BEACON_TEST_REQUIRE(observed->configuration.output_width == 2560);
  BEACON_TEST_REQUIRE(observed->configuration.output_height == 1600);
  BEACON_TEST_REQUIRE(observed->configuration.frame_rate_numerator == 120);
  BEACON_TEST_REQUIRE(observed->configuration.frame_rate_denominator == 1);
  BEACON_TEST_REQUIRE(observed->configuration.input_format ==
                      VideoPixelFormat::bgra8);
  BEACON_TEST_REQUIRE(observed->configuration.output_format ==
                      VideoPixelFormat::nv12);
  BEACON_TEST_REQUIRE(observed->configuration.input_range == VideoRange::full);
  BEACON_TEST_REQUIRE(observed->configuration.output_range ==
                      VideoRange::limited);
  BEACON_TEST_REQUIRE(observed->configuration.matrix ==
                      VideoColorMatrix::bt709);
  BEACON_TEST_REQUIRE(observed->layout.destination.top == 80);
  BEACON_TEST_REQUIRE(converted->width == 2560);
  BEACON_TEST_REQUIRE(converted->height == 1600);
  BEACON_TEST_REQUIRE(converted->qpc_timestamp == 99);
  BEACON_TEST_REQUIRE(converted->format == VideoPixelFormat::nv12);
  BEACON_TEST_REQUIRE(converted->range == VideoRange::limited);
  BEACON_TEST_REQUIRE(converted->matrix == VideoColorMatrix::bt709);
}

void released_output_textures_are_reused_without_reconfiguration() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  D3d11VideoProcessor processor{std::move(platform)};

  auto first = processor.convert(frame(), plan());
  BEACON_TEST_REQUIRE(first.has_value());
  void* first_output = first->texture->native_texture();
  first.reset();
  auto second = processor.convert(frame(2560, 1600, 43), plan());

  BEACON_TEST_REQUIRE(second.has_value());
  BEACON_TEST_REQUIRE(second->texture->native_texture() == first_output);
  BEACON_TEST_REQUIRE(observed->configure_count == 1);
  BEACON_TEST_REQUIRE(observed->create_count == 1);
  BEACON_TEST_REQUIRE(observed->blit_count == 2);
}

void held_outputs_use_a_bounded_pool_instead_of_being_overwritten() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  D3d11VideoProcessor processor{std::move(platform)};

  auto first = processor.convert(frame(), plan());
  auto second = processor.convert(frame(), plan());
  auto third = processor.convert(frame(), plan());
  BEACON_TEST_REQUIRE(first.has_value());
  BEACON_TEST_REQUIRE(second.has_value());
  BEACON_TEST_REQUIRE(third.has_value());
  BEACON_TEST_REQUIRE(first->texture != second->texture);
  BEACON_TEST_REQUIRE(second->texture != third->texture);

  BEACON_TEST_REQUIRE(!processor.convert(frame(), plan()).has_value());
  BEACON_TEST_REQUIRE(processor.failure() ==
                      D3d11VideoProcessorFailure::output_pool_exhausted);
  BEACON_TEST_REQUIRE(observed->create_count == 3);
}

void device_or_configuration_changes_reset_the_cached_processor() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  D3d11VideoProcessor processor{std::move(platform)};

  auto first = processor.convert(frame(), plan());
  BEACON_TEST_REQUIRE(first.has_value());
  first.reset();
  observed->identity = reinterpret_cast<void*>(0x4000);
  auto second = processor.convert(frame(), plan());
  BEACON_TEST_REQUIRE(second.has_value());
  second.reset();
  auto third = processor.convert(frame(1920, 1080), plan());

  BEACON_TEST_REQUIRE(third.has_value());
  BEACON_TEST_REQUIRE(observed->configure_count == 3);
  BEACON_TEST_REQUIRE(observed->reset_count == 2);
  BEACON_TEST_REQUIRE(observed->create_count == 3);
}

void unsupported_capability_failure_is_preserved_without_blitting() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  platform->configure_result =
      D3d11VideoProcessorFailure::unsupported_color_conversion;
  D3d11VideoProcessor processor{std::move(platform)};

  BEACON_TEST_REQUIRE(!processor.convert(frame(), plan()).has_value());
  BEACON_TEST_REQUIRE(processor.failure() ==
                      D3d11VideoProcessorFailure::unsupported_color_conversion);
  BEACON_TEST_REQUIRE(observed->create_count == 0);
  BEACON_TEST_REQUIRE(observed->blit_count == 0);
}

void device_loss_discards_cached_gpu_state_and_reconfigures_on_retry() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  platform->blit_result = D3d11VideoProcessorFailure::device_lost;
  D3d11VideoProcessor processor{std::move(platform)};

  BEACON_TEST_REQUIRE(!processor.convert(frame(), plan()).has_value());
  BEACON_TEST_REQUIRE(processor.failure() ==
                      D3d11VideoProcessorFailure::device_lost);
  BEACON_TEST_REQUIRE(observed->reset_count == 1);

  observed->blit_result = D3d11VideoProcessorFailure::none;
  const auto retried = processor.convert(frame(), plan());
  BEACON_TEST_REQUIRE(retried.has_value());
  BEACON_TEST_REQUIRE(observed->configure_count == 2);
  BEACON_TEST_REQUIRE(observed->create_count == 2);
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    layout_preserves_the_full_source_and_centers_the_destination();
    invalid_dimensions_fail_before_touching_the_platform();
    first_conversion_requests_the_exact_sdr_nv12_contract();
    released_output_textures_are_reused_without_reconfiguration();
    held_outputs_use_a_bounded_pool_instead_of_being_overwritten();
    device_or_configuration_changes_reset_the_cached_processor();
    unsupported_capability_failure_is_preserved_without_blitting();
    device_loss_discards_cached_gpu_state_and_reconfigures_on_retry();
  });
}
