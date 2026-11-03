import unittest
from datetime import date
import calendar_data
from calendar_data import event_span, events_on, visible_days, lanes, month_layout, event_style, contrast
from backend import model_for


def event(start, end, all_day=False, **extra):
    return dict(title="일정", start_at=start, end_at=end, all_day=all_day, **extra)


class CalendarTests(unittest.TestCase):
    def test_month_and_sunday_week(self):
        days = visible_days(date(2026, 11, 3), "month")
        self.assertEqual((days[0], days[-1], len(days)), (date(2026, 11, 1), date(2026, 12, 5), 35))
        self.assertEqual(visible_days(date(2026, 11, 3), "week")[0], date(2026, 11, 1))

    def test_all_day_inclusive_end(self):
        e = event("2026-11-03T00:00:00Z", "2026-11-05T00:00:00Z", True, start_timezone="UTC", end_timezone="UTC")
        self.assertTrue(events_on([e], date(2026, 11, 5)))
        self.assertFalse(events_on([e], date(2026, 11, 6)))
        e["end_at"] = e["start_at"]
        self.assertTrue(events_on([e], date(2026, 11, 3)))

    def test_midnight_and_time_zone(self):
        e = event("2026-11-03T14:00:00Z", "2026-11-03T15:00:00Z")
        self.assertEqual(event_span(e)[0].hour, 23)
        self.assertTrue(events_on([e], date(2026, 11, 3)))
        self.assertFalse(events_on([e], date(2026, 11, 4)))

    def test_overnight_and_zero_duration(self):
        e = event("2026-11-03T14:00:00Z", "2026-11-03T16:00:00Z")
        self.assertTrue(events_on([e], date(2026, 11, 4)))
        e["end_at"] = e["start_at"]
        self.assertTrue(events_on([e], date(2026, 11, 3)))

    def test_overlap_lanes(self):
        result = lanes([(0, 10, "a"), (5, 15, "b"), (10, 20, "c"), (25, 30, "d")])
        self.assertEqual([row[3:] for row in result], [(0, 2), (1, 2), (0, 2), (0, 1)])

    def test_multi_day_bar_and_overlapping_day(self):
        days = visible_days(date(2026, 11, 3), "week")
        trip = event("2026-10-31T00:00:00Z", "2026-11-04T00:00:00Z", True)
        meeting = event("2026-11-03T01:00:00Z", "2026-11-03T02:00:00Z")
        layout = month_layout([meeting, trip], days)
        self.assertEqual([(a, b, row) for a, b, row, _ in layout], [(0, 3, 0), (2, 2, 1)])

    def test_original_all_day_colors_and_readable_text(self):
        e = event("2026-11-03T00:00:00Z", "2026-11-03T00:00:00Z", True)
        for color in calendar_data.PALETTE + ["#777777", "#000000", "#FFFFFF"]:
            background, foreground = event_style(e, color)
            self.assertEqual(background, color)
            self.assertGreaterEqual(contrast(background, foreground), 4.5)

    def test_timed_event_tint_and_multi_day_color(self):
        e = event("2026-11-03T01:00:00Z", "2026-11-03T02:00:00Z")
        for color in calendar_data.PALETTE:
            background, foreground = event_style(e, color)
            self.assertNotEqual(background, color)
            self.assertGreaterEqual(contrast(background, foreground), 4.5)
        e["end_at"] = "2026-11-04T01:00:00Z"
        self.assertEqual(event_style(e, "#47b2f7")[0], "#47b2f7")

    def test_native_model_preserves_color_and_week_overlap(self):
        a = event("2026-11-03T01:00:00Z", "2026-11-03T02:00:00Z", label_id=3)
        b = event("2026-11-03T01:30:00Z", "2026-11-03T03:00:00Z", label_id=3)
        result = model_for([a, b], [{"id":3,"color":0x47b2f7}], date(2026,11,3), "week")
        self.assertEqual(len(result["blocks"]), 2)
        self.assertEqual([x["count"] for x in result["blocks"]], [2,2])
        self.assertEqual(result["events"][0]["color"], "#47b2f7")


if __name__ == "__main__":
    unittest.main()
