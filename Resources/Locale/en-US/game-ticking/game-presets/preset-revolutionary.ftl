## Rev Head

roles-antag-rev-head-name = Head Revolutionary
roles-antag-rev-head-objective = Your objective is to take over the station by converting people to your cause and eliminating all members of Command.

head-rev-role-greeting =
    You are a head revolutionary. Your goal is to convert crew and ensure no loyal Command make it onto the evacuation shuttle.
    Crew members have Loyalty Health Points (LHP) ranging from 0 to 100. When their LHP drops below 10, they enter a Convertable state.
    You have three tiers of codewords. Speak them in conversation to drain nearby crew LHP (damage is split among all who hear you):
      Low words (10 LHP) — easy to use in normal speech without arousing suspicion.
      Mid words (20 LHP) — more suspicious, but effective.
      High words (30 LHP) — clearly revolutionary, and will CONVERT anyone already in the Convertable state.
    There is a 10-second cooldown between uses. Radio speech deals only 20% damage.
    A flash can be used to deplete a target's LHP rapidly — one hit on a Convertable target converts them.
    Mindshields absorb 100 LHP before your damage reaches the wearer — drain the shield first, then convert.
    Viva la revolución!

head-rev-briefing =
    Speak your codewords in conversation to drain the loyalty of nearby crew.
    When crew LHP drops below 10, they are Convertable — use a high codeword or flash to complete the conversion.
    Mindshields absorb damage before the wearer takes LHP damage; drain the shield first.
    Ensure no loyal Command are alive or unrestrained on the evacuation shuttle.

head-rev-break-mindshield = The mindshield implant was destroyed!

## Rev

roles-antag-rev-name = Revolutionary
roles-antag-rev-objective = Your objective is to ensure the safety and follow the orders of the head revolutionaries, and to help them take over the station by eliminating all members of Command.

rev-break-control = {$name} has remembered their true allegiance!

rev-role-greeting =
    You are a revolutionary. You are tasked with protecting the head revolutionaries and helping them take over the station.
    The revolution must work together to kill, restrain, or convert all members of Command.
    Viva la revolución!

rev-briefing = Help the head revolutionaries kill, restrain, or convert all members of Command to take over the station.

## General

rev-title = Revolutionaries
rev-description = Revolutionaries hidden among the crew are seeking to convert others to their cause and overthrow Command.

rev-not-enough-ready-players = Not enough players readied up for the game. There were {$readyPlayersCount} players readied up out of {$minimumPlayers} needed. Can't start Revolutionaries.
rev-no-one-ready = No players readied up! Can't start Revolutionaries.
rev-no-heads = There were no Head Revolutionaries to be selected. Can't start Revolutionaries.

rev-won = The head revolutionaries survived and successfully seized control of the station.

rev-lost = All head revolutionaries have died, and Command survived.

## Shuttle-based victory outcomes

rev-major-victory = The revolution has triumphed! No loyal Command escaped on the evacuation shuttle, and more than half of Command was converted to the cause. Viva la revolución!

rev-minor-victory = The revolution succeeded in preventing Command's escape, but failed to convert the majority of them. A bittersweet victory for the revolutionaries.

crew-minor-victory = Command managed to evacuate, but the revolution was widespread — over half of Command had been converted. A narrow escape for NanoTrasen.

crew-major-victory = Command held the line! Loyal officers successfully evacuated, and the majority of Command remained unconverted. The revolution has been crushed.

rev-headrev-count = {$initialCount ->
    [one] There was one head revolutionary:
    *[other] There were {$initialCount} head revolutionaries:
}

rev-headrev-name-user = [color=#5e9cff]{$name}[/color] ([color=gray]{$username}[/color]) converted {$count} {$count ->
    [one] person
    *[other] people
}

rev-headrev-name = [color=#5e9cff]{$name}[/color] converted {$count} {$count ->
    [one] person
    *[other] people
}

## Deconverted window

rev-deconverted-title = Deconverted!
rev-deconverted-text =
    As the last head revolutionary has died, the revolution is over.

    You are no longer a revolutionary, so be nice.
rev-deconverted-confirm = Confirm
