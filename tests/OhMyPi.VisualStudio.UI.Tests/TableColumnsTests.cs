using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Tests
{
    public class TableColumnsTests
    {
        [Fact]
        public void Columns_that_fit_keep_their_natural_width()
        {
            Assert.Equal(new[] { 100.0, 50.0 }, TableColumns.Fit(new[] { 100.0, 50.0 }, new[] { 20.0, 10.0 }, 150));
            Assert.Equal(new[] { 100.0, 50.0 }, TableColumns.Fit(new[] { 100.0, 50.0 }, new[] { 20.0, 10.0 }, 400));
        }

        [Fact]
        public void Unbounded_space_keeps_the_natural_width()
        {
            Assert.Equal(new[] { 100.0, 50.0 }, TableColumns.Fit(new[] { 100.0, 50.0 }, new[] { 20.0, 10.0 }, double.PositiveInfinity));
            Assert.Equal(new[] { 100.0, 50.0 }, TableColumns.Fit(new[] { 100.0, 50.0 }, new[] { 20.0, 10.0 }, double.NaN));
        }

        [Fact]
        public void Wide_columns_shrink_proportionally_and_narrow_ones_keep_their_minimum()
        {
            var widths = TableColumns.Fit(new[] { 300.0, 100.0, 20.0 }, new[] { 50.0, 50.0, 20.0 }, 210);
            Assert.Equal(3, widths.Length);
            Assert.Equal(20.0, widths[2]);
            Assert.Equal(50.0, widths[1]);
            Assert.Equal(140.0, widths[0], 6);
            Assert.Equal(210.0, widths[0] + widths[1] + widths[2], 6);
        }

        [Fact]
        public void Shrinking_without_floors_scales_every_column_by_the_same_factor()
        {
            var widths = TableColumns.Fit(new[] { 300.0, 100.0 }, new[] { 10.0, 10.0 }, 200);
            Assert.Equal(150.0, widths[0], 6);
            Assert.Equal(50.0, widths[1], 6);
        }

        [Fact]
        public void Minimums_that_exceed_the_space_are_kept_so_the_table_overflows()
        {
            Assert.Equal(new[] { 80.0, 60.0 }, TableColumns.Fit(new[] { 300.0, 100.0 }, new[] { 80.0, 60.0 }, 100));
            Assert.Equal(new[] { 80.0, 60.0 }, TableColumns.Fit(new[] { 300.0, 100.0 }, new[] { 80.0, 60.0 }, 140));
        }
    }
}
