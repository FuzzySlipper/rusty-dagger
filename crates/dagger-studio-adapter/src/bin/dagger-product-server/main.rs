mod connected_application;
mod developer_commands;
mod diagnostics;
mod live_presentation;
mod melee_presentation;
mod product_server;

use std::{env, net::IpAddr};

use anyhow::{bail, Result};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) struct Options {
    pub(crate) encounter_gallery: bool,
    pub(crate) host: IpAddr,
    pub(crate) port: u16,
}

impl Options {
    fn parse() -> Result<Self> {
        Self::parse_arguments(env::args().skip(1))
    }

    fn parse_arguments(arguments: impl IntoIterator<Item = String>) -> Result<Self> {
        let mut encounter_gallery = false;
        let mut port = 4274;
        let mut host = IpAddr::V4(std::net::Ipv4Addr::LOCALHOST);
        for argument in arguments {
            match argument.as_str() {
                "--encounter-gallery" => encounter_gallery = true,
                value if value.starts_with("--port=") => {
                    port = value["--port=".len()..]
                        .parse::<u16>()
                        .map_err(|_| anyhow::anyhow!("invalid product-service port in {value}"))?;
                }
                value if value.starts_with("--host=") => {
                    host = value["--host=".len()..].parse::<IpAddr>().map_err(|_| {
                        anyhow::anyhow!("invalid product-service bind address in {value}")
                    })?;
                }
                _ => bail!("unknown argument {argument}"),
            }
        }
        Ok(Self {
            encounter_gallery,
            host,
            port,
        })
    }
}

fn main() -> Result<()> {
    connected_application::run(Options::parse()?)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn product_options_default_to_local_service() {
        assert_eq!(
            Options::parse_arguments(Vec::<String>::new()).expect("default options"),
            Options {
                encounter_gallery: false,
                host: IpAddr::V4(std::net::Ipv4Addr::LOCALHOST),
                port: 4274,
            }
        );
    }

    #[test]
    fn product_options_accept_product_service_flags() {
        let options = Options::parse_arguments([
            "--encounter-gallery".to_owned(),
            "--host=0.0.0.0".to_owned(),
            "--port=5123".to_owned(),
        ])
        .expect("product options");
        assert!(options.encounter_gallery);
        assert_eq!(
            options.host,
            "0.0.0.0".parse::<IpAddr>().expect("bind address")
        );
        assert_eq!(options.port, 5123);
    }

    #[test]
    fn product_options_reject_retired_shell_flags() {
        for argument in ["--browser-product", "--proof", "--proof-corrupt-resource"] {
            let error = Options::parse_arguments([argument.to_owned()])
                .expect_err("retired shell flag must be rejected");
            assert!(error.to_string().contains("unknown argument"));
        }
    }
}
