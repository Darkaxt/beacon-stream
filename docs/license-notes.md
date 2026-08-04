# License Notes

Beacon Stream is GPL-3.0 because the project may copy or adapt GPL-family source from Sunshine, Apollo, Vibeshine, or Vibepollo.

Milestone 0/1 does not copy upstream source. It creates original contracts, fake backends, tests, and documentation.

Before copying upstream code, add a row to `docs/extraction-map.md` naming:

- source repository
- file path
- license
- copied/adapted/wrapped decision
- Beacon destination path
- reason for reuse

The native encoder vendors `nvEncodeAPI.h` from FFmpeg/nv-codec-headers revision
`15ee32753c92faddbabbff11676779618fc6db7e`. Its header-specific permissive notice is
retained both in the header and in
`native/vendor/nv-codec-headers/LICENSE.nvEncodeAPI.txt`. The vendored header SHA-256 is
`8776FDDCB8FEBC6AEC4D73989B1F21831EB30306BC583DA55B4BF0C14A1DC228`.
