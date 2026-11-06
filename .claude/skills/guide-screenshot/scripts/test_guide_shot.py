#!/usr/bin/env python3
"""Run: python3 test_guide_shot.py"""
import guide_shot as g

SCREEN = (402, 874)
BUTTON = (16, 703, 370, 72)

assert g.room(BUTTON, SCREEN)["above"] == 703
tail, tip = g.arrow_line(BUTTON, "above", 80, 0.5, SCREEN)
assert tip == (201, 703 - g.GAP) and tail == (201, 703 - g.GAP - 80)
tail, tip = g.arrow_line(BUTTON, "above", 80, 0.25, SCREEN)
assert tip[0] == 16 + 370 * 0.25
tail, tip = g.arrow_line((300, 100, 50, 50), "right", 30, 0.5, SCREEN)
assert tip == (350 + g.GAP, 125) and tail == (350 + g.GAP + 30, 125)
try:
    g.arrow_line((300, 100, 50, 50), "right", 80, 0.5, SCREEN)
    raise AssertionError("an arrow past the screen edge should stop the run")
except SystemExit:
    pass

assert g.label_box((5, 500), "above", 100, 40, SCREEN)[0] == g.MARGIN  # held inside the margin
assert g.label_box((400, 500), "above", 100, 40, SCREEN)[0] == 402 - g.MARGIN - 100
try:
    g.label_box((200, 20), "above", 100, 40, SCREEN)
    raise AssertionError("a label above the screen should stop the run")
except SystemExit:
    pass

assert g.overlap((0, 0, 10, 10), (5, 5, 10, 10))
assert not g.overlap((0, 0, 10, 10), (10, 0, 10, 10))
assert g.union([(0, 0, 10, 10), (20, 5, 10, 10)]) == (0, 0, 30, 15)
print("ok")
