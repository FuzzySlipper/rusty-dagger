mod connected_application;
mod diagnostics;
mod lab_server;
mod live_presentation;
mod melee_presentation;

use std::{env, net::IpAddr};

use anyhow::{bail, Result};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) struct Options {
    pub(crate) encounter_gallery: bool,
    pub(crate) lab_host: IpAddr,
    pub(crate) lab_port: u16,
}

impl Options {
    fn parse() -> Result<Self> {
        Self::parse_arguments(env::args().skip(1))
    }

    fn parse_arguments(arguments: impl IntoIterator<Item = String>) -> Result<Self> {
        let mut encounter_gallery = false;
        let mut lab_port = 4274;
        let mut lab_host = IpAddr::V4(std::net::Ipv4Addr::LOCALHOST);
        for argument in arguments {
            match argument.as_str() {
                "--encounter-gallery" => encounter_gallery = true,
                value if value.starts_with("--lab-port=") => {
                    lab_port = value["--lab-port=".len()..]
                        .parse::<u16>()
                        .map_err(|_| anyhow::anyhow!("invalid lab port in {value}"))?;
                }
                value if value.starts_with("--lab-host=") => {
                    lab_host = value["--lab-host=".len()..]
                        .parse::<IpAddr>()
                        .map_err(|_| anyhow::anyhow!("invalid Lab bind address in {value}"))?;
                }
                _ => bail!("unknown argument {argument}"),
            }
        }
        Ok(Self {
            encounter_gallery,
            lab_host,
            lab_port,
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
    fn product_options_default_to_local_lab_service() {
        assert_eq!(
            Options::parse_arguments(Vec::<String>::new()).expect("default options"),
            Options {
                encounter_gallery: false,
                lab_host: IpAddr::V4(std::net::Ipv4Addr::LOCALHOST),
                lab_port: 4274,
            }
        );
    }

    #[test]
    fn product_options_accept_product_service_flags() {
        let options = Options::parse_arguments([
            "--encounter-gallery".to_owned(),
            "--lab-host=0.0.0.0".to_owned(),
            "--lab-port=5123".to_owned(),
        ])
        .expect("product options");
        assert!(options.encounter_gallery);
        assert_eq!(
            options.lab_host,
            "0.0.0.0".parse::<IpAddr>().expect("bind address")
        );
        assert_eq!(options.lab_port, 5123);
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
