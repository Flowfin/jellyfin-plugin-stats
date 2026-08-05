// Near miss for no-identifying-network-or-path-detail-in-the-store.
//
// A support question asks which device a play came from. The session object
// already carries the answer, and the row is being written two lines below, so
// the field is copied across without anybody deciding to keep an address.

row.DeviceName = session.DeviceName;
row.ClientName = session.Client;
row.RemoteEndPoint = session.RemoteEndPoint;
