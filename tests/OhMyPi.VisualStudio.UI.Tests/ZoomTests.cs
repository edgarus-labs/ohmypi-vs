using OhMyPi.VisualStudio.UI.Model;
using Xunit;

namespace OhMyPi.VisualStudio.UI.Tests
{
    public class ZoomTests
    {
        [Fact]
        public void Wheel_up_zooms_in_and_wheel_down_zooms_out()
        {
            Assert.True(Zoom.Step(1.0, 120) > 1.0);
            Assert.True(Zoom.Step(1.0, -120) < 1.0);
        }

        [Fact]
        public void Opposite_notches_return_to_the_same_level()
        {
            Assert.Equal(1.0, Zoom.Step(Zoom.Step(1.0, 120), -120), 6);
        }

        [Fact]
        public void Level_is_clamped_to_the_editor_range()
        {
            var level = 1.0;
            for (var i = 0; i < 100; i++) level = Zoom.Step(level, 120);
            Assert.Equal(Zoom.Max, level);
            for (var i = 0; i < 100; i++) level = Zoom.Step(level, -120);
            Assert.Equal(Zoom.Min, level);
        }

        [Fact]
        public void Zero_delta_keeps_the_level()
        {
            Assert.Equal(1.5, Zoom.Step(1.5, 0));
        }
    }
}
