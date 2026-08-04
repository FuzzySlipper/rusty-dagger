//! DFRandom.cs port: classic Daggerfall's random library (ANSI C example LCG
//! constants). Seeded sequences must match classic/DFU output exactly, so
//! per-location derivations (e.g. dungeon texture tables) reproduce the
//! classic game.

/// DFRandom static generator, expressed as explicit state (no globals).
#[derive(Debug, Clone)]
pub struct DFRandom {
    next: u64,
}

impl DFRandom {
    /// DFRandom.srand(uint seed).
    pub fn new(seed: u32) -> Self {
        DFRandom { next: seed as u64 }
    }

    /// DFRandom.rand(): LCG step, returns (next >> 16) & 0x7FFF.
    pub fn rand(&mut self) -> u32 {
        self.next = self.next.wrapping_mul(1_103_515_245).wrapping_add(12_345);
        ((self.next >> 16) & 0x7FFF) as u32
    }

    /// DFRandom.random_range_inclusive(min, max).
    pub fn random_range_inclusive(&mut self, min: i32, max: i32) -> i32 {
        (self.rand() as i32) % (max - min + 1) + min
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn matches_ansi_example_sequence() {
        // The well-known ANSI C example sequence for these constants.
        let mut rng = DFRandom::new(1);
        let got: Vec<u32> = (0..5).map(|_| rng.rand()).collect();
        assert_eq!(got, [16838, 5758, 10113, 17515, 31051]);
    }

    #[test]
    fn matches_privateers_hold_seed_sequence() {
        // Seed 50050 = Privateer's Hold dungeon LocationId (see maps.rs).
        let mut rng = DFRandom::new(50050);
        let got: Vec<u32> = (0..5).map(|_| rng.rand()).collect();
        assert_eq!(got, [29809, 1548, 11675, 10363, 3991]);
    }
}
