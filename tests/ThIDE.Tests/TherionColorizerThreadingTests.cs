// The colorizer's palettes are first used from whichever thread gets there first (a background
// classifier, a test thread) while other threads create Avalonia objects. They must come out the same
// for every thread, be built once, and be immutable, since they are read from every thread afterwards.

using Avalonia.Media;
using Therion.Syntax;
using ThIDE.Editor;

namespace ThIDE.Tests;

public sealed class TherionColorizerThreadingTests
{
    [Fact]
    public void Palettes_asked_for_from_many_threads_at_once_are_one_shared_immutable_set()
    {
        const int threads = 32;
        using var start = new Barrier(threads);
        var seen = new (IReadOnlyDictionary<TokenClassification, IBrush> Light,
            IReadOnlyDictionary<TokenClassification, IBrush> Dark, IBrush LightSel, IBrush DarkSel)[threads];
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        var workers = Enumerable.Range(0, threads).Select(i => new Thread(() =>
        {
            try
            {
                start.SignalAndWait();
                // Every other thread goes through Avalonia's property system at the same moment.
                if (i % 2 == 0)
                    for (int k = 0; k < 50; k++) _ = new SolidColorBrush(Color.FromRgb((byte)k, (byte)i, 0)).Color;
                var colorizer = new TherionColorizer();
                colorizer.SetVariant(i % 3 == 0 ? TherionColorizer.Variant.Dark : TherionColorizer.Variant.Light);
                seen[i] = (TherionColorizer.Palette(TherionColorizer.Variant.Light),
                    TherionColorizer.Palette(TherionColorizer.Variant.Dark),
                    TherionColorizer.SelectionBrush(TherionColorizer.Variant.Light),
                    TherionColorizer.SelectionBrush(TherionColorizer.Variant.Dark));
            }
            catch (Exception ex) { errors.Enqueue(ex); }
        })).ToList();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());

        Assert.Empty(errors);
        var first = seen[0];
        Assert.All(seen, s =>
        {
            Assert.Same(first.Light, s.Light);
            Assert.Same(first.Dark, s.Dark);
            Assert.Same(first.LightSel, s.LightSel);
            Assert.Same(first.DarkSel, s.DarkSel);
        });
        Assert.All(first.Light.Values.Concat(first.Dark.Values).Append(first.LightSel).Append(first.DarkSel),
            b => Assert.IsAssignableFrom<IImmutableBrush>(b));
        Assert.NotSame(first.Light, first.Dark);
    }
}
