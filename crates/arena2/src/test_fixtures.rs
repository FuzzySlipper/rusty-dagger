pub fn named_bsa(records: &[(&str, &[u8])]) -> Vec<u8> {
    let mut data = Vec::new();
    data.extend_from_slice(&(records.len() as i16).to_le_bytes());
    data.extend_from_slice(&0x0100u16.to_le_bytes());
    for (_, bytes) in records {
        data.extend_from_slice(bytes);
    }
    for (name, bytes) in records {
        let mut encoded_name = [0u8; 14];
        let name = name.as_bytes();
        assert!(name.len() <= encoded_name.len());
        encoded_name[..name.len()].copy_from_slice(name);
        data.extend_from_slice(&encoded_name);
        data.extend_from_slice(&(bytes.len() as i32).to_le_bytes());
    }
    data
}

pub fn numeric_bsa(records: &[(u32, &[u8])]) -> Vec<u8> {
    let mut data = Vec::new();
    data.extend_from_slice(&(records.len() as i16).to_le_bytes());
    data.extend_from_slice(&0x0200u16.to_le_bytes());
    for (_, bytes) in records {
        data.extend_from_slice(bytes);
    }
    for (id, bytes) in records {
        data.extend_from_slice(&id.to_le_bytes());
        data.extend_from_slice(&(bytes.len() as i32).to_le_bytes());
    }
    data
}

pub fn constant_pak(value: u8) -> Vec<u8> {
    let header_len = crate::pak::PAK_HEIGHT * 4;
    let mut data = vec![0u8; header_len];
    for row in 0..crate::pak::PAK_HEIGHT {
        let offset = header_len + row * 3;
        data[row * 4..row * 4 + 4].copy_from_slice(&(offset as u32).to_le_bytes());
        data.extend_from_slice(&(crate::pak::PAK_WIDTH as u16).to_le_bytes());
        data.push(value);
    }
    data
}
