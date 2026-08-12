//! Read-only DAGGER.SND parser (DFU `SndFile.cs`).
//!
//! The file is a numeric BSA whose records are raw unsigned 8-bit mono PCM at
//! 11025 Hz. DFU's `SoundClips` values address directory order, not record ID.

use std::path::Path;

use crate::bsa::BsaArchive;

pub const SAMPLE_RATE: u32 = 11_025;

pub struct SndFile {
    archive: BsaArchive,
}

impl SndFile {
    pub fn load(path: &Path) -> std::io::Result<Self> {
        Ok(Self {
            archive: BsaArchive::load(path)?,
        })
    }

    pub fn len(&self) -> usize {
        self.archive.len()
    }

    pub fn is_empty(&self) -> bool {
        self.archive.is_empty()
    }

    pub fn record_id(&self, index: usize) -> Option<u32> {
        self.archive.record_id(index)
    }

    pub fn pcm_u8(&self, index: usize) -> Option<&[u8]> {
        self.archive.get_index(index)
    }

    pub fn wav_bytes(&self, index: usize) -> Result<Vec<u8>, String> {
        let pcm = self
            .pcm_u8(index)
            .ok_or_else(|| format!("DAGGER.SND index {index} out of range"))?;
        let data_len = u32::try_from(pcm.len()).map_err(|_| "sound record too large")?;
        let riff_len = data_len
            .checked_add(36)
            .ok_or_else(|| "WAV length overflow".to_string())?;
        let mut wav = Vec::with_capacity(pcm.len() + 44);
        wav.extend_from_slice(b"RIFF");
        wav.extend_from_slice(&riff_len.to_le_bytes());
        wav.extend_from_slice(b"WAVEfmt ");
        wav.extend_from_slice(&16u32.to_le_bytes());
        wav.extend_from_slice(&1u16.to_le_bytes());
        wav.extend_from_slice(&1u16.to_le_bytes());
        wav.extend_from_slice(&SAMPLE_RATE.to_le_bytes());
        wav.extend_from_slice(&SAMPLE_RATE.to_le_bytes());
        wav.extend_from_slice(&1u16.to_le_bytes());
        wav.extend_from_slice(&8u16.to_le_bytes());
        wav.extend_from_slice(b"data");
        wav.extend_from_slice(&data_len.to_le_bytes());
        wav.extend_from_slice(pcm);
        Ok(wav)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn configured_classic_combat_clips_exercise_real_sound_records() {
        let path = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../local/arena2/DAGGER.SND");
        if !path.exists() {
            eprintln!(
                "skipping real Arena2 sound check: {} is absent",
                path.display()
            );
            return;
        }
        let sounds = SndFile::load(&path).expect("parse real DAGGER.SND");
        for index in [106usize, 108, 109, 110, 111, 112] {
            let pcm = sounds.pcm_u8(index).expect("classic combat clip");
            assert!(!pcm.is_empty());
            let wav = sounds.wav_bytes(index).expect("wrap combat clip as WAV");
            assert_eq!(&wav[..4], b"RIFF");
            assert_eq!(&wav[8..12], b"WAVE");
            assert_eq!(wav.len(), pcm.len() + 44);
        }
    }
}
