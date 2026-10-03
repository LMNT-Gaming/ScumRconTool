# SurvivalStatsNativeProbe

Read-only native runtime validation probe for SCUM Server build `24472735`
(`1.3.2.2.137892`). It hooks `SCUMServer.exe + 0x1AA8970`, calls the original
function first, then logs a small confirmed prefix of its output structure.

The probe refuses to install unless all 40 expected entry bytes match. It never
changes a statistic and exposes no network endpoint.

The identifier in the log is SCUM's internal `user_profile_id`, not the Steam
ID. Steam-ID correlation is a later, separate hook after this candidate passes
runtime validation.
