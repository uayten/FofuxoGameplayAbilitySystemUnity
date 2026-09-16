using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem.Editor;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The geometry the timeline drag depends on. A marker that reads back one
    /// frame off from where it was dropped is the failure this pins: it is
    /// invisible in a screenshot and wrong in the asset.
    /// </summary>
    public sealed class AbilityTimelineLayoutTests
    {
        private static readonly Rect Lane = new(20f, 0f, 300f, 16f);

        [Test]
        public void FrameWidth_DividesTheLaneByTheFrameCount()
        {
            Assert.AreEqual(10f, AbilityTimelineLayout.FrameWidth(Lane, 30), 0.0001f);
        }

        [Test]
        public void FrameWidth_IsZeroWithoutFrames()
        {
            Assert.AreEqual(0f, AbilityTimelineLayout.FrameWidth(Lane, 0), 0.0001f);
        }

        [Test]
        public void MarkerX_SitsAtTheCentreOfTheFrameCell()
        {
            Assert.AreEqual(25f, AbilityTimelineLayout.MarkerX(Lane, 30, 1), 0.0001f);
            Assert.AreEqual(315f, AbilityTimelineLayout.MarkerX(Lane, 30, 30), 0.0001f);
        }

        [Test]
        public void FrameAt_InvertsMarkerX_ForEveryFrame()
        {
            const int Frames = 37;
            for (int frame = 1; frame <= Frames; frame++)
            {
                float x = AbilityTimelineLayout.MarkerX(Lane, Frames, frame);
                Assert.AreEqual(
                    frame,
                    AbilityTimelineLayout.FrameAt(Lane, Frames, x),
                    $"frame {frame} did not survive the round trip");
            }
        }

        [Test]
        public void FrameAt_ClampsToTheLane()
        {
            Assert.AreEqual(1, AbilityTimelineLayout.FrameAt(Lane, 30, Lane.x - 500f));
            Assert.AreEqual(30, AbilityTimelineLayout.FrameAt(Lane, 30, Lane.xMax + 500f));
        }

        [Test]
        public void FrameAt_IsZeroWithoutFrames()
        {
            Assert.AreEqual(0, AbilityTimelineLayout.FrameAt(Lane, 0, Lane.center.x));
        }

        [Test]
        public void Span_CoversItsCellsEdgeToEdge()
        {
            Rect span = AbilityTimelineLayout.Span(Lane, 30, 11, 20);
            Assert.AreEqual(120f, span.x, 0.0001f);
            Assert.AreEqual(100f, span.width, 0.0001f);
        }

        [Test]
        public void Span_OfOneFrame_IsOneCellWide()
        {
            Rect span = AbilityTimelineLayout.Span(Lane, 30, 7, 7);
            Assert.AreEqual(10f, span.width, 0.0001f);
            Assert.AreEqual(
                AbilityTimelineLayout.MarkerX(Lane, 30, 7),
                span.center.x,
                0.0001f);
        }

        [Test]
        public void Span_ClampsToTheLane()
        {
            Rect span = AbilityTimelineLayout.Span(Lane, 30, -5, 400);
            Assert.AreEqual(Lane.x, span.x, 0.0001f);
            Assert.AreEqual(Lane.width, span.width, 0.0001f);
        }

        [Test]
        public void Span_OfAnInvertedRange_HasNoWidth()
        {
            Rect span = AbilityTimelineLayout.Span(Lane, 30, 20, 11);
            Assert.AreEqual(0f, span.width, 0.0001f);
        }

        [Test]
        public void Span_WithoutFrames_HasNoWidth()
        {
            Rect span = AbilityTimelineLayout.Span(Lane, 0, 1, 10);
            Assert.AreEqual(0f, span.width, 0.0001f);
        }
    }
}
